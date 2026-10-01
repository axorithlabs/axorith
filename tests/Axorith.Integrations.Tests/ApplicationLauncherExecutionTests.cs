using System.Diagnostics;
using System.Runtime.Versioning;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform;
using Xunit;

namespace Axorith.Integrations.Tests;

using LauncherModule = Axorith.Module.ApplicationLauncher.Module;

public sealed class ApplicationLauncherExecutionTests
{
    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task StartsTheConfiguredExecutableAndTerminatesItWhenTheSessionEnds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AxorithLauncher", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var processName = $"axorith-launch-{Guid.NewGuid():N}";
        var executable = Path.Combine(directory, processName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), executable);

        var module = new LauncherModule(new TestModuleLogger(), new EmptyAppDiscovery(), new NoopNotifier(),
            new TestHttpClientFactory(), new EmptySecureStorage(), PlatformServices.CreateProcessService(),
            new ImmediateWindowService());
        try
        {
            await module.InitializeAsync(CancellationToken.None);
            var settings = module.GetSettings().ToDictionary(setting => setting.Key);
            settings["ApplicationPath"].SetValueFromString("custom-app");
            settings["CustomPath"].SetValueFromString(executable);
            settings["ApplicationArgs"].SetValueFromString("-t 127.0.0.1");
            settings["ProcessMode"].SetValueFromString("LaunchNew");
            settings["LifecycleMode"].SetValueFromString("TerminateForce");
            Assert.Equal(Axorith.Sdk.ValidationStatus.Ok,
                (await module.ValidateSettingsAsync(CancellationToken.None)).Status);

            await module.OnSessionStartAsync(CancellationToken.None);

            using var process = await WaitForProcessAsync(processName, TimeSpan.FromSeconds(5));
            Assert.NotNull(process);
            Assert.False(process.HasExited);

            await module.OnSessionEndAsync(CancellationToken.None);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(process.HasExited);
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
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<Process?> WaitForProcessAsync(string processName, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            var process = Process.GetProcessesByName(processName).FirstOrDefault();
            if (process is not null)
                return process;
            await Task.Delay(20);
        }

        return null;
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
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

    private sealed class EmptySecureStorage : ISecureStorageService
    {
        public void StoreSecret(string key, string secret) { }
        public string? RetrieveSecret(string key) => null;
        public void DeleteSecret(string key) { }
    }

    private sealed class NoopNotifier : INotifier
    {
        public void ShowToast(string message, NotificationType type = NotificationType.Info) { }
        public Task ShowSystemAsync(string title, string message, TimeSpan? expiration = null) => Task.CompletedTask;
    }

    private sealed class TestModuleLogger : IModuleLogger
    {
        public void LogDebug(string messageTemplate, params object[] args) { }
        public void LogInfo(string messageTemplate, params object[] args) { }
        public void LogWarning(string messageTemplate, params object[] args) { }
        public void LogError(Exception? exception, string messageTemplate, params object[] args) { }
        public void LogFatal(Exception? exception, string messageTemplate, params object[] args) { }
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
