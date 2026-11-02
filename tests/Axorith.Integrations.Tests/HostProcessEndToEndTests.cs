using System.Diagnostics;
using System.Text.Json;
using Axorith.Client.CoreSdk;
using Axorith.Contracts;
using Axorith.Core.Models;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Xunit;

namespace Axorith.Integrations.Tests;

[Collection("SiteBlocker extension pipes")]
public sealed partial class HostProcessEndToEndTests
{
    [Theory]
    [InlineData("App Blocker", "AppBlocker", "blocked_apps.json")]
    [InlineData("Site Blocker", "SiteBlocker", "blocked_sites.json")]
    public async Task BlockerCatalogsLoadAcrossConcurrentEditorsAndHostRestart(
        string moduleName, string moduleDirectory, string dataFile)
    {
        using var data = new TemporaryDirectory();
        var buildRoot = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            await File.ReadAllTextAsync(Path.Combine(buildRoot, "Modules", moduleDirectory, "Data", dataFile)))!;
        Assert.NotEmpty(catalog);
        for (var restart = 0; restart < 2; restart++)
        {
            await using var host = await HostProcess.StartAsync(data.Path);
            var module = Assert.Single(await host.Modules.ListModulesAsync(), module => module.Name == moduleName);
            var editors = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
            try
            {
                var results = await Task.WhenAll(editors.Select(id => host.Modules.BeginEditAsync(
                    module.Id, id, new Dictionary<string, object?>()))).WaitAsync(TimeSpan.FromSeconds(10));
                foreach (var result in results)
                {
                    Assert.True(result.Result.Success, result.Result.Message);
                    var setting = Assert.Single(result.SettingsInfo.Settings, setting => setting.Key == "Categories");
                    Assert.Equal(catalog.Keys.Order(), setting.Choices.Select(choice => choice.Key).Order());
                    Assert.All(setting.Choices, choice => Assert.False(string.IsNullOrWhiteSpace(choice.Value)));
                    Assert.Equal(string.Empty, setting.CurrentValue);
                }
            }
            finally
            {
                foreach (var id in editors) await host.Modules.EndEditAsync(id);
            }
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task ClientApisPersistPresetAndScheduleAcrossHostRestartAndShutdownReleasesTheLauncher()
    {
        using var data = new TemporaryDirectory();
        Guid presetId;
        Guid scheduleId;
        var processName = "axorith-host-process-" + Guid.NewGuid().ToString("N");
        var executable = Path.Combine(data.Path, processName + (OperatingSystem.IsWindows() ? ".exe" : ""));
        File.Copy(OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "ping.exe") : "/bin/sleep", executable);
        Process? launched = null;
        try
        {
            await using (var host = await HostProcess.StartAsync(data.Path))
            {
                var modules = await host.Modules.ListModulesAsync();
                var launcher = Assert.Single(modules, module => module.Name == "Application Launcher");
                var preset = await host.Presets.CreatePresetAsync(new SessionPreset
                {
                    Id = Guid.NewGuid(), Name = "Persisted process flow",
                    Modules = [new Axorith.Core.Models.ConfiguredModule
                    {
                        ModuleId = launcher.Id,
                        Settings = new Dictionary<string, string>
                        {
                            ["ApplicationPath"] = "custom-app", ["CustomPath"] = executable,
                            ["ApplicationArgs"] = OperatingSystem.IsWindows() ? "-t 127.0.0.1" : "120",
                            ["ProcessMode"] = "LaunchNew", ["LifecycleMode"] = "TerminateForce"
                        }
                    }]
                });
                presetId = preset.Id;
                preset.Name = "Updated through Client API";
                await host.Presets.UpdatePresetAsync(preset);
                var schedule = await host.Scheduler.CreateScheduleAsync(new SessionSchedule
                {
                    Id = Guid.NewGuid(), PresetId = presetId, Name = "Tomorrow", Type = ScheduleType.OneTime,
                    OneTimeDate = DateTimeOffset.Now.AddDays(1)
                });
                scheduleId = schedule.Id;
                await host.StopAsync();
            }

            await using (var host = await HostProcess.StartAsync(data.Path))
            {
                var loaded = await host.Presets.GetPresetAsync(presetId);
                Assert.NotNull(loaded);
                Assert.Equal("Updated through Client API", loaded.Name);
                Assert.Equal("TerminateForce", Assert.Single(loaded.Modules).Settings["LifecycleMode"]);
                Assert.Contains(await host.Scheduler.ListSchedulesAsync(), schedule => schedule.Id == scheduleId && schedule.PresetId == presetId);
                var result = await host.Sessions.StartSessionAsync(presetId, Guid.NewGuid());
                Assert.True(result.Success, result.Message);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (launched is null)
                {
                    foreach (var process in Process.GetProcessesByName(processName))
                    {
                        if (launched is null) launched = process;
                        else process.Dispose();
                    }
                    if (launched is null) await Task.Delay(20, timeout.Token);
                }
                Assert.True((await host.Sessions.GetCurrentSessionAsync())!.IsActive);
                await host.StopAsync();
                await launched.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(launched.HasExited);
            }

            await using (var host = await HostProcess.StartAsync(data.Path))
            {
                Assert.Contains(await host.Sessions.GetSessionHistoryAsync(), activity => activity.PresetName == "Updated through Client API");
                await host.Scheduler.DeleteScheduleAsync(scheduleId);
                await host.Presets.DeletePresetAsync(presetId);
                await host.StopAsync();
            }
            await using (var host = await HostProcess.StartAsync(data.Path))
            {
                Assert.Null(await host.Presets.GetPresetAsync(presetId));
                Assert.DoesNotContain(await host.Scheduler.ListSchedulesAsync(), schedule => schedule.Id == scheduleId);
                await host.StopAsync();
            }
        }
        finally
        {
            if (launched is not null)
            {
                if (!launched.HasExited) { launched.Kill(); await launched.WaitForExitAsync(); }
                launched.Dispose();
            }
        }
    }

    private sealed class HostProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _output;
        private readonly Task<string> _error;
        private readonly string _logPath;
        private GrpcChannel? _channel;
        private HttpClient? _http;
        private bool _stopped;
        public GrpcPresetsApi Presets { get; private set; } = null!;
        public GrpcSchedulerApi Scheduler { get; private set; } = null!;
        public GrpcModulesApi Modules { get; private set; } = null!;
        public GrpcSessionsApi Sessions { get; private set; } = null!;

        private HostProcess(Process process, string logPath)
        {
            _process = process;
            _output = process.StandardOutput.ReadToEndAsync();
            _error = process.StandardError.ReadToEndAsync();
            _logPath = logPath;
        }

        public static async Task<HostProcess> StartAsync(string data, string? chromiumExtensionId = null, bool edgeBrowser = false)
        {
            var installedRoot = Environment.GetEnvironmentVariable("AXORITH_E2E_INSTALL_ROOT");
            var buildRoot = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
            var root = installedRoot ?? buildRoot;
            Directory.CreateDirectory(Path.Combine(data, "config"));
            await File.WriteAllTextAsync(Path.Combine(data, "config", "telemetry-preference.txt"), "false");
            var hostDirectory = Path.Combine(root, "Axorith.Host");
            var start = new ProcessStartInfo(Path.Combine(hostDirectory, OperatingSystem.IsWindows() ? "Axorith.Host.exe" : "Axorith.Host"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, WorkingDirectory = hostDirectory
            };
            start.Environment["AXORITH_ROAMING_ROOT"] = data;
            start.Environment["AXORITH_LOCAL_ROOT"] = Path.Combine(data, "local");
            start.Environment["AXORITH_TEST_MODE"] = installedRoot is null ? "1" : "0";
            start.Environment["AXORITH_DEV"] = "1";
            start.Environment["AXORITH_TELEMETRY_DISABLED"] = "1";
            start.Environment["AXORITH_TELEMETRY_API_KEY"] = "##E2E_DISABLED##";
            start.Environment["AXORITH_TELEMETRY_HOST"] = "http://127.0.0.1:9";
            start.Environment["AXORITH_TEST_HOST_MUTEX"] = "Local\\AxorithE2E-" + Guid.NewGuid().ToString("N");
            foreach (var argument in new[]
            {
                "--environment", "Development", "--Grpc:Port", "0",
                "--Persistence:ConfigPath", Path.Combine(data, "config"),
                "--Persistence:PresetsPath", Path.Combine(data, "presets"),
                "--Persistence:LogsPath", Path.Combine(data, "logs"),
                "--Persistence:HostInfoPath", Path.Combine(data, "host-info.json"),
                "--Modules:SearchPaths:0", Path.Combine(root, "Modules")
            }) start.ArgumentList.Add(argument);
            if (chromiumExtensionId is not null)
            {
                start.ArgumentList.Add(edgeBrowser ? "--SiteBlocker:NativeMessaging:EdgeExtensionIds" : "--SiteBlocker:NativeMessaging:ChromiumExtensionIds");
                start.ArgumentList.Add(chromiumExtensionId);
            }
            File.Delete(Path.Combine(data, "host-info.json"));
            var host = new HostProcess(Process.Start(start)!, Path.Combine(buildRoot, "host-process-e2e.log"));
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var infoPath = Path.Combine(data, "host-info.json");
                int port;
                while (!Axorith.Client.Services.HostInfoReader.TryReadPort(infoPath, out port))
                {
                    if (host._process.HasExited) throw new InvalidOperationException(await host._output + await host._error);
                    await Task.Delay(25, timeout.Token);
                }
                var address = $"http://127.0.0.1:{port}";
                var token = await File.ReadAllTextAsync(Path.Combine(data, "config", ".auth_token"), timeout.Token);
                host._http = new HttpClient { BaseAddress = new Uri(address) };
                host._channel = TestGrpc.CreateAuthenticatedChannel(host._http, token);
                var retry = Policy.Handle<Exception>().RetryAsync(0);
                host.Presets = new GrpcPresetsApi(new PresetsService.PresetsServiceClient(host._channel), retry);
                host.Scheduler = new GrpcSchedulerApi(new SchedulerService.SchedulerServiceClient(host._channel), retry);
                host.Modules = new GrpcModulesApi(new ModulesService.ModulesServiceClient(host._channel), retry, NullLogger.Instance);
                host.Sessions = new GrpcSessionsApi(new SessionsService.SessionsServiceClient(host._channel), retry, NullLogger.Instance);
                var health = await new DiagnosticsService.DiagnosticsServiceClient(host._channel).GetHealthAsync(new HealthCheckRequest(), cancellationToken: timeout.Token);
                Assert.True(health.LoadedModules >= 4, "The real Host did not load its installed modules.");
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }

        public async Task StopAsync()
        {
            if (_stopped) return;
            var response = await new HostManagement.HostManagementClient(_channel!).RequestShutdownAsync(new ShutdownRequest { Reason = "Functional test" });
            Assert.True(response.Accepted, response.Message);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, _process.ExitCode);
            var logs = await _output + await _error;
            Assert.DoesNotContain("ObjectDisposedException", logs);
            Assert.DoesNotContain("Unhandled exception", logs, StringComparison.OrdinalIgnoreCase);
            _stopped = true;
        }

        public async Task CrashAsync()
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(_process.HasExited);
        }

        public async ValueTask DisposeAsync()
        {
            try { Sessions?.Dispose(); Modules?.Dispose(); _channel?.Dispose(); _http?.Dispose(); }
            finally
            {
                if (!_process.HasExited) { _process.Kill(entireProcessTree: true); await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                try
                {
                    await File.AppendAllTextAsync(_logPath, $"\n=== Host {_process.Id}: {DateTimeOffset.UtcNow:O} ===\n" + await _output + await _error);
                }
                finally { _process.Dispose(); }
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("axorith-process-e2e-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
