using System.Diagnostics;
using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Axorith.Client.CoreSdk;
using Axorith.Client.ViewModels;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Xunit;

namespace Axorith.Integrations.Tests;

[CollectionDefinition("Dashboard Host", DisableParallelization = true)]
public sealed class DashboardHostCollection;

[Collection("Dashboard Host")]
public sealed class SessionDashboardRegressionTests
{
    private sealed class RealHost(string dataPath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:ConfigPath"] = Path.Combine(dataPath, "config"),
                    ["Persistence:PresetsPath"] = Path.Combine(dataPath, "presets"),
                    ["Persistence:LogsPath"] = Path.Combine(dataPath, "logs"),
                    ["Modules:SearchPaths:0"] = Path.Combine(dataPath, "modules"),
                    ["Modules:SearchPaths:1"] = Path.Combine(dataPath, "empty"),
                    ["Modules:SearchPaths:2"] = Path.Combine(dataPath, "empty"),
                    ["Modules:SearchPaths:3"] = Path.Combine(dataPath, "empty"),
                    ["Modules:SearchPaths:4"] = Path.Combine(dataPath, "empty")
                }));
        }

        public GrpcChannel Connect(string dataPath)
        {
            var client = CreateDefaultClient();
            var token = File.ReadAllText(Path.Combine(dataPath, "config", ".auth_token"));
            return TestGrpc.CreateAuthenticatedChannel(client, token);
        }
    }

    [AvaloniaFact]
    public async Task HomeHistorySurvivesClientAndHostRestartAndIncludesSessionsCompletedWithoutUi()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "AxorithDashboard", Guid.NewGuid().ToString("N"));
        var historyPath = Path.Combine(dataPath, "config", "session-history.json");
        var modulePath = Path.Combine(dataPath, "modules", "SiteBlocker");
        Directory.CreateDirectory(modulePath);
        File.Copy(typeof(Axorith.Module.SiteBlocker.Module).Assembly.Location,
            Path.Combine(modulePath, "Axorith.Module.SiteBlocker.dll"));
        var moduleId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(modulePath, "module.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            id = moduleId, name = "Site Blocker", category = "Productivity", platforms = new[] { "Windows" },
            assembly = "Axorith.Module.SiteBlocker.dll"
        }));
        var now = DateTimeOffset.Now;
        new SessionHistoryStore(historyPath).Add(new SessionActivity(now.AddMinutes(-90), now.AddMinutes(-30), "Deep work"));

        await using (var host = new RealHost(dataPath))
        using (var channel = host.Connect(dataPath))
        {
            var manager = host.Services.GetRequiredService<ISessionManager>();
            await ((ModuleRegistry)host.Services.GetRequiredService<IModuleRegistry>()).InitializeAsync(CancellationToken.None);
            await manager.StartSessionAsync(new SessionPreset
            {
                Id = Guid.NewGuid(), Name = "While UI closed",
                Modules = [new Axorith.Core.Models.ConfiguredModule
                {
                    ModuleId = moduleId, Settings = new Dictionary<string, string> { ["Categories"] = "[]" }
                }]
            });
            await manager.StopCurrentSessionAsync();
            Assert.Equal(2, manager.SessionHistory.Count);
        }

        await using (var host = new RealHost(dataPath))
        using (var channel = host.Connect(dataPath))
        using (var api = new GrpcSessionsApi(new SessionsService.SessionsServiceClient(channel),
                   Policy.Handle<Exception>().RetryAsync(0), NullLogger.Instance))
        using (var services = new ServiceCollection().BuildServiceProvider())
        {
            var history = await api.GetSessionHistoryAsync();
            Assert.Equal(2, history.Count);
            for (var reopen = 0; reopen < 2; reopen++)
            {
                using var vm = new MainViewModel(new ShellViewModel(), null!, api, null!, services);
                await RefreshState(vm);
                Assert.Equal(2, vm.SessionsThisWeek);
                Assert.Equal("1 h 0 m", vm.FocusThisWeekLabel);
                Assert.Equal(2, vm.RecentSessions.Count);
                Assert.Contains(vm.RecentSessions, session => session.StartsWith("Deep work", StringComparison.Ordinal));
                Assert.True(vm.HasSessionActivity);
                Assert.InRange(vm.FocusActivityValues.Sum(), 60, 61);
                if (now.AddMinutes(-90).Date == now.Date) Assert.Equal("1 h 0 m", vm.FocusTodayLabel);
            }
        }
    }

    [AvaloniaFact]
    public async Task NormalSessionShowsCountdownAndLiveOverviewRefreshesAfterStopWithoutReopening()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "AxorithDashboard", Guid.NewGuid().ToString("N"));
        var moduleId = InstallModule(dataPath, typeof(Axorith.Module.SiteBlocker.Module), "Site Blocker");
        await using var host = new RealHost(dataPath);
        using var channel = host.Connect(dataPath);
        using var api = new GrpcSessionsApi(new SessionsService.SessionsServiceClient(channel),
            Policy.Handle<Exception>().RetryAsync(0), NullLogger.Instance);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var vm = new MainViewModel(new ShellViewModel(), null!, api, null!, services);
        var manager = host.Services.GetRequiredService<ISessionManager>();
        await ((ModuleRegistry)host.Services.GetRequiredService<IModuleRegistry>()).InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.Now;
        new SessionHistoryStore(Path.Combine(dataPath, "config", "session-history.json"))
            .Add(new SessionActivity(now.AddMinutes(-15), now.AddMinutes(-5), "Saved work"));
        await RefreshState(vm);
        Assert.Equal(1, vm.SessionsThisWeek);
        Assert.Equal("10 min", vm.FocusThisWeekLabel);
        await manager.StartSessionAsync(new SessionPreset
        {
            Id = Guid.NewGuid(), Name = "Normal focus",
            Modules = [new Axorith.Core.Models.ConfiguredModule
            {
                ModuleId = moduleId, Settings = new Dictionary<string, string> { ["Categories"] = "" }
            }]
        });
        await RefreshState(vm);
        Assert.Matches(@"^\d+:\d{2}$", vm.ActiveSessionRemaining);
        Assert.Equal("Elapsed", vm.ActiveSessionEndTime);
        await Task.Run(() => host.Services.GetRequiredService<ISessionAutoStopService>()
            .StartTrackingAsync(manager.CurrentSessionInstanceId!.Value, TimeSpan.FromMinutes(3), null));
        await RefreshState(vm);
        Assert.Contains(vm.ActiveSessionRemaining, new[] { "03:00", "02:59", "02:58" });
        Assert.StartsWith("Ends at", vm.ActiveSessionEndTime);
        Assert.Equal(2, vm.SessionsThisWeek);
        await host.Services.GetRequiredService<ISessionAutoStopService>().StopTrackingAsync();
        var fixedStop = new SessionSchedule
        {
            PresetId = manager.ActiveSession!.Id, Type = ScheduleType.StopRecurring,
            RecurringTime = DateTimeOffset.Now.LocalDateTime.AddMinutes(2).TimeOfDay
        };
        await host.Services.GetRequiredService<IScheduleManager>().SaveScheduleAsync(fixedStop, CancellationToken.None);
        await RefreshState(vm);
        Assert.Contains(vm.ActiveSessionRemaining, new[] { "02:00", "01:59", "01:58" });
        Assert.StartsWith("Ends at", vm.ActiveSessionEndTime);
        await manager.StopCurrentSessionAsync();
        await WaitUntil(() => !vm.IsSessionActive && vm.SessionsThisWeek == 2 && vm.RecentSessions.Count == 2);
        Assert.False(vm.IsSessionActive);
        Assert.Equal(2, vm.SessionsThisWeek);
        Assert.Equal(2, vm.RecentSessions.Count);
        Assert.Equal(2, new SessionHistoryStore(Path.Combine(dataPath, "config", "session-history.json")).Read().Count);
        Assert.InRange(vm.FocusActivityValues.Sum(), 10, 11);
    }

    [AvaloniaFact]
    public async Task AppPickerPersistsNewlineListAndClosesProcessesLaunchedAfterSessionStart()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "AxorithBlocker", Guid.NewGuid().ToString("N"));
        var moduleId = InstallModule(dataPath, typeof(Axorith.Module.AppBlocker.Module), "App Blocker");
        await using var host = new RealHost(dataPath);
        using var channel = host.Connect(dataPath);
        using var api = new GrpcModulesApi(new ModulesService.ModulesServiceClient(channel),
            Policy.Handle<Exception>().RetryAsync(0), NullLogger.Instance);
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = (ModuleRegistry)host.Services.GetRequiredService<IModuleRegistry>();
        await registry.InitializeAsync(CancellationToken.None);
        var model = new Axorith.Core.Models.ConfiguredModule
        {
            ModuleId = moduleId, Settings = new Dictionary<string, string> { ["Categories"] = "" }
        };
        using var vm = new ConfiguredModuleViewModel((await api.ListModulesAsync()).Single(), model, api, services);
        await WaitUntil(() => !vm.IsLoading);
        var picker = vm.Settings.Single(setting => setting.Setting.Key == "AppToAdd");
        var names = new[] { "waitfor", "axonenote" + Guid.NewGuid().ToString("N")[..6] };
        foreach (var name in names)
        {
            await ((ReactiveUI.ReactiveCommand<KeyValuePair<string, string>, System.Reactive.Unit>)picker.SelectChoiceCommand)
                .Execute(new KeyValuePair<string, string>(name, name));
        }
        await WaitUntil(() => vm.Settings.Single(setting => setting.Setting.Key == "CustomProcessList")
            .StringValue.Contains(names[1], StringComparison.Ordinal));
        vm.SaveChangesToModel();
        Assert.Equal(string.Join(Environment.NewLine, names), model.Settings["CustomProcessList"]);
        var preset = new SessionPreset { Id = Guid.NewGuid(), Name = "Block selected apps", Modules = [model] };
        var presets = host.Services.GetRequiredService<IPresetManager>();
        await presets.SavePresetAsync(preset, CancellationToken.None);
        var loaded = (await presets.GetPresetByIdAsync(preset.Id, CancellationToken.None))!;
        Assert.Equal(model.Settings["CustomProcessList"], loaded.Modules[0].Settings["CustomProcessList"]);
        var manager = host.Services.GetRequiredService<ISessionManager>();
        await manager.StartSessionAsync(loaded);
        try
        {
            var waitFor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "waitfor.exe");
            using var process = Process.Start(new ProcessStartInfo(waitFor, $"/t 60 Axorith{Guid.NewGuid():N}")
                { UseShellExecute = false, CreateNoWindow = true })!;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await process.WaitForExitAsync(timeout.Token);
                Assert.NotEqual(0, process.ExitCode);
            }
            finally
            {
                if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            }
        }
        finally
        {
            await manager.StopCurrentSessionAsync();
        }
    }

    [Fact]
    public void OverviewReadsDiskChangesAndSplitsSessionsAcrossLocalDays()
    {
        var path = Path.Combine(Path.GetTempPath(), "AxorithDashboard", Guid.NewGuid().ToString("N"), "history.json");
        var store = new SessionHistoryStore(path);
        Assert.Empty(store.Read());
        var today = DateTimeOffset.Now.LocalDateTime.Date;
        var midnight = new DateTimeOffset(today, TimeZoneInfo.Local.GetUtcOffset(today));
        new SessionHistoryStore(path).Add(new SessionActivity(midnight.AddMinutes(-30), midnight.AddMinutes(30), "Across midnight"));
        var overview = SessionOverview.Calculate(store.Read(), null, "", midnight.AddHours(1));
        Assert.Equal(TimeSpan.FromMinutes(30), overview.Today);
        Assert.Equal(TimeSpan.FromMinutes(60), overview.Week);
        Assert.Equal(30, overview.DailyMinutes[^1]);
        Assert.Equal(30, overview.DailyMinutes[^2]);
        Assert.Equal(1, overview.SessionCount);
        File.WriteAllText(path, "broken history");
        Assert.Throws<System.Text.Json.JsonException>(() => store.Add(new SessionActivity(midnight, midnight.AddHours(1), "New")));
        Assert.Equal("broken history", File.ReadAllText(path));
    }

    private static Guid InstallModule(string dataPath, Type moduleType, string name)
    {
        var modulePath = Path.Combine(dataPath, "modules", "TestModule");
        Directory.CreateDirectory(modulePath);
        var assemblyName = Path.GetFileName(moduleType.Assembly.Location);
        File.Copy(moduleType.Assembly.Location, Path.Combine(modulePath, assemblyName));
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(modulePath, "module.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            id, name, category = "Productivity", platforms = new[] { "Windows" }, assembly = assemblyName
        }));
        return id;
    }

    private static async Task WaitUntil(Func<bool> ready)
    {
        var until = Stopwatch.StartNew();
        while (!ready() && until.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(ready());
    }

    private static Task RefreshState(MainViewModel vm) => vm.InitializeAsync();

}
