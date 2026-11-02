using System.Diagnostics;
using System.Runtime.Versioning;
using Axorith.Sdk.Logging;
using Axorith.Shared.Platform;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed partial class ApplicationLauncherExecutionTests
{
    [FirefoxTheory]
    [InlineData("LaunchNew", "TerminateForce", false, true)]
    [InlineData("LaunchNew", "TerminateGraceful", false, true)]
    [InlineData("LaunchNew", "KeepRunning", false, false)]
    [InlineData("LaunchOrAttach", "TerminateForce", false, true)]
    [InlineData("LaunchOrAttach", "TerminateForce", true, false)]
    [InlineData("AttachExisting", "TerminateForce", true, false)]
    [InlineData("LaunchNew", "TerminateForce", true, false)]
    [SupportedOSPlatform("windows")]
    public async Task FirefoxHandoffPreservesSessionOwnership(string mode, string lifecycle,
        bool alreadyRunning, bool shouldExit)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AxorithFirefox", Guid.NewGuid().ToString("N"));
        var installation = Path.Combine(directory, "installation");
        var profile = Path.Combine(directory, "profile");
        Directory.CreateDirectory(profile);
        // A private installation also isolates process discovery from the user's running Firefox.
        var source = Path.GetDirectoryName(FirefoxExecutable)!;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(installation, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        await File.WriteAllTextAsync(Path.Combine(profile, "user.js"), """
            user_pref("browser.shell.checkDefaultBrowser", false);
            user_pref("browser.startup.firstrunSkipsHomepage", true);
            user_pref("browser.aboutwelcome.enabled", false);
            user_pref("browser.sessionstore.resume_from_crash", false);
            user_pref("browser.startup.homepage_override.mstone", "ignore");
            user_pref("browser.tabs.warnOnClose", false);
            user_pref("browser.warnOnQuit", false);
            user_pref("datareporting.policy.dataSubmissionEnabled", false);
            user_pref("toolkit.telemetry.enabled", false);
            """);
        var executable = Path.Combine(installation, "firefox.exe");
        var arguments = $"-launcher -force-launcher -profile \"{profile}\" about:blank";
        var processes = PlatformServices.CreateProcessService();
        var logger = new FirefoxLogger(output);
        using var module = new Axorith.Module.ApplicationLauncher.Module(logger, new EmptyAppDiscovery(),
            new NoopNotifier(), new TestHttpClientFactory(), new EmptySecureStorage(), processes,
            PlatformServices.CreateWindowService());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            if (alreadyRunning)
            {
                using var initial = processes.StartProcess(new ProcessStartInfo(executable, arguments)
                    { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })!;
                using var ready = await WaitForBrowserWindowAsync();
            }
            await module.InitializeAsync(deadline.Token);
            var settings = module.GetSettings().ToDictionary(setting => setting.Key);
            settings["ApplicationPath"].SetValueFromString(executable);
            settings["AdditionalArgs"].SetValueFromString(arguments);
            settings["ProcessMode"].SetValueFromString(mode);
            settings["LifecycleMode"].SetValueFromString(lifecycle);
            settings["BringToForeground"].SetValueFromString("false");
            Assert.Equal(Axorith.Sdk.ValidationStatus.Ok,
                (await module.ValidateSettingsAsync(deadline.Token)).Status);
            await module.OnSessionStartAsync(deadline.Token);
            using var browser = await WaitForBrowserWindowAsync();
            Assert.False(browser.HasExited);
            if (!alreadyRunning)
            {
                Assert.NotNull(logger.LaunchedProcessId);
                Assert.NotEqual(logger.LaunchedProcessId, browser.Id);
            }

            await module.OnSessionEndAsync(deadline.Token);
            module.Dispose();
            if (shouldExit)
            {
                await browser.WaitForExitAsync(deadline.Token).WaitAsync(TimeSpan.FromSeconds(10), deadline.Token);
                while (true)
                {
                    var remaining = processes.FindProcesses(executable);
                    var count = remaining.Count;
                    foreach (var process in remaining) process.Dispose();
                    if (count == 0) break;
                    await Task.Delay(50, deadline.Token);
                }
            }
            else
            {
                await Task.Delay(300, deadline.Token);
                Assert.False(browser.HasExited, "Pre-existing/KeepRunning Firefox must survive stop and disposal.");
            }
            Assert.Empty(logger.Errors);
        }
        finally
        {
            foreach (var process in processes.FindProcesses(executable))
            {
                using (process)
                {
                    if (process.HasExited) continue;
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            await DeleteDirectoryAsync(directory);
        }

        async Task<Process> WaitForBrowserWindowAsync()
        {
            while (true)
            {
                var found = processes.FindProcesses(executable);
                var window = found.Find(process => process.MainWindowHandle != IntPtr.Zero);
                foreach (var process in found)
                    if (!ReferenceEquals(process, window)) process.Dispose();
                if (window is not null) return window;
                await Task.Delay(50, deadline.Token);
            }
        }
    }

    private static string FirefoxExecutable => Environment.GetEnvironmentVariable("AXORITH_E2E_FIREFOX_EXE") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe");

    private sealed class FirefoxTheoryAttribute : TheoryAttribute
    {
        public FirefoxTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(FirefoxExecutable))
                Skip = "Requires Windows and Firefox; uses a private installation and profile.";
        }
    }

    private sealed class FirefoxLogger(Xunit.Abstractions.ITestOutputHelper output) : IModuleLogger
    {
        public int? LaunchedProcessId { get; private set; }
        public List<string> Errors { get; } = [];
        public void LogDebug(string messageTemplate, params object[] args) { }
        public void LogInfo(string messageTemplate, params object[] args)
        {
            output.WriteLine(messageTemplate + " " + string.Join(", ", args));
            if (messageTemplate == "Process {ProcessName} (PID: {ProcessId}) launched successfully")
                LaunchedProcessId = (int)args[1];
        }
        public void LogWarning(string messageTemplate, params object[] args) =>
            output.WriteLine(messageTemplate + " " + string.Join(", ", args));
        public void LogError(Exception? exception, string messageTemplate, params object[] args) =>
            Errors.Add(messageTemplate + exception);
        public void LogFatal(Exception? exception, string messageTemplate, params object[] args) =>
            Errors.Add(messageTemplate + exception);
    }
}
