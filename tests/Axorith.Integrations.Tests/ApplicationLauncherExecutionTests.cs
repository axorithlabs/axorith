using System.Diagnostics;
using System.Runtime.Versioning;
using Axorith.Shared.Platform;
using Xunit;
using Xunit.Abstractions;

namespace Axorith.Integrations.Tests;

using LauncherModule = Axorith.Module.ApplicationLauncher.Module;

public sealed partial class ApplicationLauncherExecutionTests(ITestOutputHelper output)
{
    [WindowsTheory]
    [InlineData("LaunchNew", "TerminateForce", false, true)]
    [InlineData("LaunchNew", "KeepRunning", false, false)]
    [InlineData("LaunchNew", "TerminateGraceful", false, false)]
    [InlineData("LaunchOrAttach", "TerminateForce", false, true)]
    [InlineData("LaunchOrAttach", "TerminateForce", true, false)]
    [InlineData("AttachExisting", "TerminateForce", true, false)]
    [InlineData("AttachExisting", "TerminateForce", false, false)]
    [InlineData("LaunchNew", "TerminateForce", true, true)]
    [InlineData("LaunchNew", "TerminateForce", false, true, true)]
    [SupportedOSPlatform("windows")]
    public async Task RealProcessLifetimeRespectsLaunchAttachAndStopModes(string mode, string lifecycle,
        bool alreadyRunning, bool shouldExit, bool disposeOnly = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AxorithLauncher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var processName = $"axorith-launch-{Guid.NewGuid():N}";
        var executable = Path.Combine(directory, processName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), executable);

        var module = new LauncherModule(new TestModuleLogger(), new EmptyAppDiscovery(), new NoopNotifier(),
            new TestHttpClientFactory(), new EmptySecureStorage(), PlatformServices.CreateProcessService(),
            new ImmediateWindowService());
        var existingId = 0;
        try
        {
            if (alreadyRunning)
            {
                using var existing = Process.Start(new ProcessStartInfo(executable, "-t 127.0.0.1")
                    { UseShellExecute = false, CreateNoWindow = true })!;
                existingId = existing.Id;
            }
            await module.InitializeAsync(CancellationToken.None);
            var settings = module.GetSettings().ToDictionary(setting => setting.Key);
            settings["ApplicationPath"].SetValueFromString("custom-app");
            settings["CustomPath"].SetValueFromString(executable);
            settings["ApplicationArgs"].SetValueFromString("-t 127.0.0.1");
            settings["ProcessMode"].SetValueFromString(mode);
            settings["LifecycleMode"].SetValueFromString(lifecycle);
            Assert.Equal(Axorith.Sdk.ValidationStatus.Ok,
                (await module.ValidateSettingsAsync(CancellationToken.None)).Status);

            await module.OnSessionStartAsync(CancellationToken.None);
            if (mode == "AttachExisting" && !alreadyRunning)
            {
                await module.OnSessionEndAsync(CancellationToken.None);
                Assert.Empty(Process.GetProcessesByName(processName));
                return;
            }

            using var process = await WaitForProcessAsync(processName, TimeSpan.FromSeconds(5),
                mode == "LaunchNew" ? existingId : 0);
            Assert.NotNull(process);
            Assert.False(process.HasExited);
            var running = Process.GetProcessesByName(processName);
            Assert.Equal(mode == "LaunchNew" && alreadyRunning ? 2 : 1, running.Length);
            foreach (var item in running) item.Dispose();

            if (disposeOnly) module.Dispose();
            else await module.OnSessionEndAsync(CancellationToken.None);
            if (shouldExit)
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(process.HasExited);
            }
            else
            {
                module.Dispose();
                await Task.Delay(250);
                Assert.False(process.HasExited, "Attached/KeepRunning processes must survive; graceful close must not force-kill a console app.");
            }
            if (alreadyRunning)
            {
                using var existing = Process.GetProcessById(existingId);
                Assert.False(existing.HasExited, "A pre-existing process must survive even when LaunchNew kills another instance.");
            }
        }
        finally
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                }
            }

            module.Dispose();
            await DeleteDirectoryAsync(directory);
        }
    }

    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    [SupportedOSPlatform("windows")]
    public async Task ForceStopTerminatesTheLaunchedProcessTree(bool disposeOnly)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AxorithLauncher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var childName = $"axorith-child-{Guid.NewGuid():N}";
        var childPath = Path.Combine(directory, childName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), childPath);
        var script = Path.Combine(directory, "parent.ps1");
        await File.WriteAllTextAsync(script, """
            param($ChildPath)
            $child = Start-Process -FilePath $ChildPath -ArgumentList '-t 127.0.0.1' -WindowStyle Hidden -PassThru
            $child.WaitForExit()
            """);
        using var module = new LauncherModule(new TestModuleLogger(), new EmptyAppDiscovery(), new NoopNotifier(),
            new TestHttpClientFactory(), new EmptySecureStorage(), PlatformServices.CreateProcessService(),
            new ImmediateWindowService());
        try
        {
            await module.InitializeAsync(CancellationToken.None);
            var settings = module.GetSettings().ToDictionary(setting => setting.Key);
            settings["ApplicationPath"].SetValueFromString("custom-app");
            settings["CustomPath"].SetValueFromString(Path.Combine(Environment.SystemDirectory,
                "WindowsPowerShell", "v1.0", "powershell.exe"));
            settings["ApplicationArgs"].SetValueFromString(
                $"-NoProfile -NonInteractive -WindowStyle Hidden -File \"{script}\" \"{childPath}\"");
            settings["ProcessMode"].SetValueFromString("LaunchNew");
            settings["LifecycleMode"].SetValueFromString("TerminateForce");
            await module.OnSessionStartAsync(CancellationToken.None);
            using var child = await WaitForProcessAsync(childName, TimeSpan.FromSeconds(10));
            Assert.NotNull(child);
            if (disposeOnly) module.Dispose();
            else await module.OnSessionEndAsync(CancellationToken.None);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(child.HasExited);
        }
        finally
        {
            module.Dispose();
            foreach (var process in Process.GetProcessesByName(childName))
            {
                using (process)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            await DeleteDirectoryAsync(directory);
        }
    }

    private static async Task DeleteDirectoryAsync(string directory)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) &&
                                       timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                // Windows can briefly keep the executable locked after the process exits.
                await Task.Delay(50);
            }
        }
    }

    private static async Task<Process?> WaitForProcessAsync(string processName, TimeSpan timeout, int excludedId = 0)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            var processes = Process.GetProcessesByName(processName);
            var process = processes.FirstOrDefault(candidate => candidate.Id != excludedId);
            foreach (var candidate in processes)
                if (!ReferenceEquals(candidate, process)) candidate.Dispose();
            if (process is not null)
                return process;
            await Task.Delay(20);
        }

        return null;
    }

    private sealed class WindowsTheoryAttribute : TheoryAttribute
    {
        public WindowsTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "The real executable launch check requires Windows.";
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class EmptyAppDiscovery : IAppDiscoveryService
    {
        public string? FindKnownApp(params string[] processNames) => null;
        public List<AppInfo> FindAppsByPublisher(string publisherName) => [];
        public List<AppInfo> GetInstalledApplicationsIndex() => [];
    }

    private sealed class ImmediateWindowService : IPlatformWindowService
    {
        public Task WaitForWindowInitAsync(Process process, int timeoutMs = 5000,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void MoveWindowToMonitor(IntPtr windowHandle, int monitorIndex) { }
        public void SetWindowState(IntPtr windowHandle, WindowState state) { }
        public WindowState GetWindowState(IntPtr windowHandle) => WindowState.Normal;
        public void SetWindowSize(IntPtr windowHandle, int width, int height) { }
        public void SetWindowPosition(IntPtr windowHandle, int x, int y) { }
        public (int X, int Y, int Width, int Height) GetWindowBounds(IntPtr windowHandle) => (0, 0, 0, 0);
        public void FocusWindow(IntPtr windowHandle) { }
        public int GetMonitorCount() => 0;
        public (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex) => (0, 0, 0, 0);
        public string GetMonitorName(int monitorIndex) => string.Empty;
    }
}
