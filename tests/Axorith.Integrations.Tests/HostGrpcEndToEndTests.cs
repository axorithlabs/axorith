using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Axorith.Client.CoreSdk;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Host.Streaming;
using FluentAssertions;
using Empty = Google.Protobuf.WellKnownTypes.Empty;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;
using INotifier = Axorith.Sdk.Services.INotifier;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using System.Runtime.CompilerServices;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Axorith.Integrations.Tests.HostGrpcEndToEndTests.TestAppBuilder))]

namespace Axorith.Integrations.Tests;

public sealed class HostTestFactory : WebApplicationFactory<Program>
{
    public string TestDataPath { get; }
    public Guid SiteBlockerModuleId { get; }
    public Guid AppBlockerModuleId { get; }

    public HostTestFactory()
    {
        TestDataPath = Path.Combine(Path.GetTempPath(), "AxorithTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(TestDataPath);
        SiteBlockerModuleId = Guid.NewGuid();
        AppBlockerModuleId = Guid.NewGuid();
        InstallModule(typeof(Axorith.Module.SiteBlocker.Module), SiteBlockerModuleId, "Site Blocker");
        InstallModule(typeof(Axorith.Module.AppBlocker.Module), AppBlockerModuleId, "App Blocker");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var testConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:PresetsPath"] = Path.Combine(TestDataPath, "presets"),
                    ["Persistence:LogsPath"] = Path.Combine(TestDataPath, "logs"),
                    ["Persistence:ConfigPath"] = Path.Combine(TestDataPath, "config"),
                    ["Persistence:HostInfoPath"] = Path.Combine(TestDataPath, "host-info.json"),
                    ["Modules:SearchPaths:0"] = Path.Combine(TestDataPath, "modules"),
                    ["Modules:SearchPaths:1"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:2"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:3"] = Path.Combine(TestDataPath, "empty_modules"),
                    ["Modules:SearchPaths:4"] = Path.Combine(TestDataPath, "empty_modules")
                })
                .Build();

            configBuilder.AddConfiguration(testConfig);
        });

    }

    private void InstallModule(Type moduleType, Guid id, string name)
    {
        var moduleDirectory = Path.Combine(TestDataPath, "modules", name.Replace(' ', '_'));
        Directory.CreateDirectory(moduleDirectory);
        var assemblyName = Path.GetFileName(moduleType.Assembly.Location);
        File.Copy(moduleType.Assembly.Location, Path.Combine(moduleDirectory, assemblyName));
        File.WriteAllText(Path.Combine(moduleDirectory, "module.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            id,
            name,
            category = "Productivity",
            platforms = new[] { OperatingSystem.IsWindows() ? "Windows" : "Linux" },
            assembly = assemblyName
        }));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            if (Directory.Exists(TestDataPath))
            {
                Directory.Delete(TestDataPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

[Collection("SiteBlocker extension pipes")]
public class HostGrpcEndToEndTests(HostTestFactory factory) : IClassFixture<HostTestFactory>
{
    [AvaloniaFact]
    public void PresetSummaryClarifiesDurationAndOmitsDurationStopSchedule()
    {
        using var preset = new SessionPresetViewModel(new SessionPreset
        {
            Name = "main",
            FocusCommitment = new Axorith.Core.Models.FocusCommitmentOptions
            {
                EndCondition = Axorith.Core.Models.FocusEndCondition.Duration,
                Duration = TimeSpan.FromMinutes(5)
            }
        }, [], null!, null!,
        [new SessionSchedule { Name = "main Duration Stop", Type = ScheduleType.StopDuration }]);

        Assert.Equal("Session duration: 5 min", preset.SessionSummary);
        Assert.False(preset.HasSchedule);
        Assert.Empty(preset.ScheduleSummary);
    }

    static HostGrpcEndToEndTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    private async Task<(
        DiagnosticsService.DiagnosticsServiceClient diagnostics,
        PresetsService.PresetsServiceClient presets,
        SessionsService.SessionsServiceClient sessions,
        ModulesService.ModulesServiceClient modules,
        GrpcChannel channel)> CreateAuthenticatedClientsAsync()
    {
        var httpClient = factory.CreateDefaultClient();

        var tokenPath = Path.Combine(factory.TestDataPath, "config", ".auth_token");

        var token = string.Empty;
        for (var i = 0; i < 50; i++)
        {
            if (File.Exists(tokenPath))
            {
                try
                {
                    using var fs = new FileStream(tokenPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    token = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        break;
                    }
                }
                catch
                {
                    /* ignore and retry */
                }
            }

            await Task.Delay(100);
        }

        if (string.IsNullOrEmpty(token))
        {
            throw new FileNotFoundException(
                $"Auth token not found at {tokenPath}. Host failed to start or write token.");
        }

        var channel = TestGrpc.CreateAuthenticatedChannel(httpClient, token);

        return (
            new DiagnosticsService.DiagnosticsServiceClient(channel),
            new PresetsService.PresetsServiceClient(channel),
            new SessionsService.SessionsServiceClient(channel),
            new ModulesService.ModulesServiceClient(channel),
            channel
        );
    }

    [Fact]
    public async Task Diagnostics_GetHealth_ShouldReturnHealthy()
    {
        var (diagnostics, _, _, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var response = await diagnostics.GetHealthAsync(new HealthCheckRequest());

                response.Status.Should().Be(HealthStatus.Healthy);
            response.Version.Should().NotBeNullOrEmpty();
            response.LoadedModules.Should().Be(2);
            response.ActiveSessions.Should().Be(0);
        }
    }

    [Fact]
    public async Task GrpcRejectsRequestsWithoutTheHostAuthenticationToken()
    {
        using var httpClient = factory.CreateDefaultClient();
        Assert.True(File.Exists(Path.Combine(factory.TestDataPath, "host-info.json")),
            "The Host must write discovery data inside the isolated test directory.");
        using var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        var diagnostics = new DiagnosticsService.DiagnosticsServiceClient(channel);

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            diagnostics.GetHealthAsync(new HealthCheckRequest()).ResponseAsync);

        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
    }

    [Fact]
    public async Task Presets_Create_List_Get_Delete_ShouldRoundTrip()
    {
        var (_, presets, _, modules, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var name = $"IntegrationTest-{Guid.NewGuid():N}";
            var endDays = new[] { DayOfWeek.Monday, DayOfWeek.Friday };

            var installedModule = await GetInstalledSiteBlockerAsync(modules);
            var configuredModule = new Axorith.Contracts.ConfiguredModule
            {
                ModuleId = installedModule.Id,
                InstanceId = Guid.NewGuid().ToString(),
                CustomName = "Site Blocker"
            };
            configuredModule.Settings.Add("Mode", "BlockList");
            configuredModule.Settings.Add("Categories", "[]");
            configuredModule.Settings.Add("CustomSites", "focus.example");

            var requestPreset = new Preset
            {
                Name = name,
                FocusCommitment = new Axorith.Contracts.FocusCommitmentOptions
                {
                    Mode = (Axorith.Contracts.FocusCommitmentMode)1,
                    EndCondition = (Axorith.Contracts.FocusEndCondition)2,
                    HasEndAtLocalTime = true,
                    EndAtHour = 18,
                    EndAtMinute = 30,
                    AfterEnd = (Axorith.Contracts.AfterEndBehavior)0,
                    EndAtDaysOfWeek = { endDays.Select(day => (int)day) }
                }
            };
            requestPreset.Modules.Add(configuredModule);

            var created = await presets.CreatePresetAsync(new CreatePresetRequest { Preset = requestPreset });

            created.Should().NotBeNull();
            created.Name.Should().Be(name);
            Guid.TryParse(created.Id, out _).Should().BeTrue();

            var list = await presets.ListPresetsAsync(new ListPresetsRequest());
            list.Presets.Should().NotBeNull();
            list.Presets.Should().Contain(p => p.Id == created.Id);

            var fetched = await presets.GetPresetAsync(new GetPresetRequest
            {
                PresetId = created.Id
            });

            fetched.Should().NotBeNull();
            fetched.Id.Should().Be(created.Id);
            fetched.Name.Should().Be(name);
            fetched.FocusCommitment.EndAtDaysOfWeek.Should().Equal(endDays.Select(day => (int)day));
            fetched.Modules.Should().ContainSingle();
            fetched.Modules[0].ModuleId.Should().Be(installedModule.Id);
            fetched.Modules[0].InstanceId.Should().Be(configuredModule.InstanceId);
            fetched.Modules[0].Settings["CustomSites"].Should().Be("focus.example");

            fetched.Name = $"{name}-updated";
            fetched.Modules[0].Settings["CustomSites"] = "updated.example";
            var updated = await presets.UpdatePresetAsync(new UpdatePresetRequest { Preset = fetched });
            updated.Name.Should().Be($"{name}-updated");
            var updatedFromDisk = await presets.GetPresetAsync(new GetPresetRequest { PresetId = created.Id });
            updatedFromDisk.Modules[0].Settings["CustomSites"].Should().Be("updated.example");

            await presets.DeletePresetAsync(new DeletePresetRequest
            {
                PresetId = created.Id
            });

            var afterDelete = await presets.ListPresetsAsync(new ListPresetsRequest());
            afterDelete.Presets.Should().NotContain(p => p.Id == created.Id);
        }
    }

    [Fact]
    public async Task RunningModuleSettingChangesAreStreamedToTheConnectedClient()
    {
        var (_, presets, sessions, modules, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            var module = await GetInstalledSiteBlockerAsync(modules);
            var instanceId = Guid.NewGuid();
            var preset = new Preset { Name = $"SettingStream-{Guid.NewGuid():N}" };
            var configuredModule = new Axorith.Contracts.ConfiguredModule
            {
                ModuleId = module.Id,
                InstanceId = instanceId.ToString()
            };
            configuredModule.Settings.Add("Mode", "BlockList");
            configuredModule.Settings.Add("Categories", "[]");
            configuredModule.Settings.Add("CustomSites", string.Empty);
            preset.Modules.Add(configuredModule);

            var created = await presets.CreatePresetAsync(new CreatePresetRequest { Preset = preset });
            var started = await sessions.StartSessionAsync(new StartSessionRequest { PresetId = created.Id });
            started.Success.Should().BeTrue(started.Message);

            try
            {
                using var stream = modules.StreamSettingUpdates(
                    new StreamSettingUpdatesRequest { ModuleInstanceId = instanceId.ToString() },
                    cancellationToken: timeout.Token);
                var updateTask = ReadSettingUpdateAsync(stream.ResponseStream, "CustomSites", "sync-marker.example",
                    timeout.Token);

                var updated = await modules.UpdateSettingAsync(new UpdateSettingRequest
                {
                    ModuleInstanceId = instanceId.ToString(),
                    SettingKey = "CustomSites",
                    StringValue = "sync-marker.example"
                });

                updated.Success.Should().BeTrue(updated.Message);
                var update = await updateTask.WaitAsync(TimeSpan.FromSeconds(5));
                update.ModuleInstanceId.Should().Be(instanceId.ToString());
                update.SettingKey.Should().Be("CustomSites");
                update.StringValue.Should().Be("sync-marker.example");
            }
            finally
            {
                timeout.Cancel();
                await sessions.StopSessionAsync(new StopSessionRequest());
                await presets.DeletePresetAsync(new DeletePresetRequest { PresetId = created.Id });
            }
        }
    }

    [Fact]
    public async Task InstalledModulesReturnTheirRealSettingsAndActionsOverGrpc()
    {
        var (_, _, _, modules, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        {
            await GetInstalledSiteBlockerAsync(modules);
            var productivityModules = await modules.ListModulesAsync(new ListModulesRequest
            {
                Category = "Productivity"
            });
            Assert.Contains(productivityModules.Modules, module => module.Id == factory.SiteBlockerModuleId.ToString());
            Assert.Contains(productivityModules.Modules, module => module.Id == factory.AppBlockerModuleId.ToString());
            var unknownCategory = await modules.ListModulesAsync(new ListModulesRequest { Category = "Unknown" });
            Assert.Empty(unknownCategory.Modules);

            foreach (var (moduleId, expectedAction) in new[]
                     {
                         (factory.SiteBlockerModuleId, "InstallExtension.Firefox"),
                         (factory.AppBlockerModuleId, "AddApp")
                     })
            {
                var response = await modules.GetModuleSettingsAsync(new GetModuleSettingsRequest
                {
                    ModuleId = moduleId.ToString()
                });

                Assert.NotEmpty(response.Settings);
                Assert.Contains(response.Actions, action => action.Key == expectedAction);
            }

            var siteBlockerSettings = await modules.GetModuleSettingsAsync(new GetModuleSettingsRequest
            {
                ModuleId = factory.SiteBlockerModuleId.ToString()
            });
            Assert.DoesNotContain(siteBlockerSettings.Actions, action => action.Key == "InstallExtension.Chrome");
        }
    }

    [Fact]
    public async Task SiteBlockerExtensionWarningSurvivesGrpcValidation()
    {
        var (_, _, _, modules, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        {
            await GetInstalledSiteBlockerAsync(modules);
            var result = await modules.ValidateSettingsAsync(new ValidateSettingsRequest
            {
                ModuleId = factory.SiteBlockerModuleId.ToString(),
                ModuleInstanceId = Guid.NewGuid().ToString()
            });

            Assert.True(result.IsValid, result.Message);
            Assert.True(result.IsWarning);
            Assert.Contains("extension", result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [WindowsFact]
    public async Task RunningAppBlockerActionUpdatesItsRealSettingThroughGrpc()
    {
        var (_, presets, sessions, modules, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            var instanceId = Guid.NewGuid();
            var configuredModule = new Axorith.Contracts.ConfiguredModule
            {
                ModuleId = factory.AppBlockerModuleId.ToString(),
                InstanceId = instanceId.ToString()
            };
            configuredModule.Settings.Add("Mode", "BlockList");
            configuredModule.Settings.Add("Categories", "[]");
            configuredModule.Settings.Add("CustomProcessList", string.Empty);
            var preset = new Preset { Name = $"Action-{Guid.NewGuid():N}" };
            preset.Modules.Add(configuredModule);
            var created = await presets.CreatePresetAsync(new CreatePresetRequest { Preset = preset });
            var started = await sessions.StartSessionAsync(new StartSessionRequest { PresetId = created.Id });
            started.Success.Should().BeTrue(started.Message);

            try
            {
                var state = await sessions.GetSessionStateAsync(new GetSessionStateRequest());
                Assert.True(state.IsActive);
                Assert.Equal(created.Id, state.PresetId);
                Assert.Contains(state.ModuleStates, moduleState => moduleState.InstanceId == instanceId.ToString());

                var hostManagement = new HostManagement.HostManagementClient(channel);
                var hostStatus = await hostManagement.GetStatusAsync(new Empty());
                Assert.True(hostStatus.IsSessionRunning);
                Assert.Equal(1, hostStatus.ActiveModulesCount);
                Assert.Equal(created.Id, hostStatus.CurrentPresetId);
                var diagnostics = new DiagnosticsService.DiagnosticsServiceClient(channel);
                var health = await diagnostics.GetHealthAsync(new HealthCheckRequest());
                Assert.Equal(1, health.ActiveSessions);
                Assert.Equal(2, health.LoadedModules);

                using var stream = modules.StreamSettingUpdates(
                    new StreamSettingUpdatesRequest { ModuleInstanceId = instanceId.ToString() },
                    cancellationToken: timeout.Token);
                var expectedProcessName = $"axorith-no-process-{Guid.NewGuid():N}";
                var updateTask = ReadSettingUpdateAsync(stream.ResponseStream, "CustomProcessList",
                    expectedProcessName, timeout.Token);

                var selection = await modules.UpdateSettingAsync(new UpdateSettingRequest
                {
                    ModuleInstanceId = instanceId.ToString(),
                    SettingKey = "AppToAdd",
                    StringValue = expectedProcessName
                });
                selection.Success.Should().BeTrue(selection.Message);

                var invoked = await modules.InvokeActionAsync(new InvokeActionRequest
                {
                    ModuleInstanceId = instanceId.ToString(),
                    ActionKey = "AddApp"
                });

                invoked.Success.Should().BeTrue(invoked.Message);
                var update = await updateTask.WaitAsync(TimeSpan.FromSeconds(5));
                update.StringValue.Should().Be(expectedProcessName);
            }
            finally
            {
                timeout.Cancel();
                await sessions.StopSessionAsync(new StopSessionRequest());
                await presets.DeletePresetAsync(new DeletePresetRequest { PresetId = created.Id });
            }
        }
    }

    [Fact]
    public async Task SchedulerGrpcCrudPersistsARealSchedule()
    {
        var (_, presets, _, _, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        {
            var scheduler = new SchedulerService.SchedulerServiceClient(channel);
            var preset = await presets.CreatePresetAsync(new CreatePresetRequest
            {
                Preset = new Preset { Name = $"Schedule-{Guid.NewGuid():N}" }
            });
            string? scheduleId = null;

            try
            {
                var date = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(1));
                var created = await scheduler.CreateScheduleAsync(new CreateScheduleRequest
                {
                    Schedule = new Axorith.Contracts.Schedule
                    {
                        PresetId = preset.Id,
                        Name = "Tomorrow's focus session",
                        Type = (int)Axorith.Core.Models.ScheduleType.OneTime,
                        OneTimeDate = date,
                        AutoStopDurationSeconds = 3600
                    }
                });
                scheduleId = created.Id;
                Assert.True(Guid.TryParse(created.Id, out _));
                Assert.Equal("Tomorrow's focus session", created.Name);
                Assert.Equal(3600, created.AutoStopDurationSeconds);

                var listed = await scheduler.ListSchedulesAsync(new ListSchedulesRequest());
                Assert.Contains(listed.Schedules, schedule => schedule.Id == created.Id);

                var disabled = await scheduler.SetEnabledAsync(new SetScheduleEnabledRequest
                {
                    ScheduleId = created.Id,
                    Enabled = false
                });
                Assert.False(disabled.IsEnabled);

                var updated = await scheduler.UpdateScheduleAsync(new UpdateScheduleRequest
                {
                    Schedule = new Axorith.Contracts.Schedule
                    {
                        Id = created.Id,
                        PresetId = preset.Id,
                        Name = "Updated focus session",
                        IsEnabled = false,
                        Type = (int)Axorith.Core.Models.ScheduleType.OneTime,
                        OneTimeDate = date,
                        AutoStopDurationSeconds = 1800
                    }
                });
                Assert.Equal("Updated focus session", updated.Name);
                Assert.Equal(1800, updated.AutoStopDurationSeconds);
                Assert.False(updated.IsEnabled);

                var lockStatus = await scheduler.GetConfigurationLockStatusAsync(
                    new ConfigurationLockStatusRequest { PresetId = preset.Id });
                Assert.False(lockStatus.IsLocked);

                await scheduler.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = created.Id });
                scheduleId = null;
                var afterDelete = await scheduler.ListSchedulesAsync(new ListSchedulesRequest());
                Assert.DoesNotContain(afterDelete.Schedules, schedule => schedule.Id == created.Id);
            }
            finally
            {
                if (scheduleId is not null)
                    await scheduler.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = scheduleId });
                await presets.DeletePresetAsync(new DeletePresetRequest { PresetId = preset.Id });
            }
        }
    }

    [WindowsFact]
    public Task OneTimeScheduleStartsItsPresetAtTheScheduledTime() =>
        AssertScheduleStartsPresetAsync("OneTimeStart", "Start in a few seconds", ScheduleType.OneTime,
            schedule => schedule.OneTimeDate =
                Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddSeconds(4)));

    [WindowsFact]
    public Task RecurringScheduleStartsItsPresetAtTheScheduledTime() =>
        AssertScheduleStartsPresetAsync("RecurringStart", "Start at recurring time", ScheduleType.Recurring,
            schedule => schedule.RecurringTime = DateTimeOffset.Now.AddSeconds(5).TimeOfDay
                .ToString("c", System.Globalization.CultureInfo.InvariantCulture));

    [WindowsFact]
    public async Task StopRecurringScheduleEndsItsActivePresetAtTheScheduledTime()
    {
        var (_, presets, sessions, _, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        {
            var scheduler = new SchedulerService.SchedulerServiceClient(channel);
            var preset = await CreateAppBlockerPresetAsync(presets, "RecurringStop");
            string? scheduleId = null;
            var started = false;

            try
            {
                var start = await sessions.StartSessionAsync(new StartSessionRequest { PresetId = preset.Id });
                Assert.True(start.Success, start.Message);
                started = true;

                var runAt = DateTimeOffset.Now.AddSeconds(5).TimeOfDay;
                var schedule = await CreateScheduleAsync(scheduler, preset.Id, "Stop at recurring time",
                    ScheduleType.StopRecurring, schedule =>
                        schedule.RecurringTime = runAt.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
                scheduleId = schedule.Id;

                var state = await WaitForSessionStateAsync(sessions, value => !value.IsActive,
                    TimeSpan.FromSeconds(10));
                Assert.False(state.IsActive);
                started = false;
            }
            finally
            {
                if (started)
                    await sessions.StopSessionAsync(new StopSessionRequest());
                if (scheduleId is not null)
                    await scheduler.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = scheduleId });
                await presets.DeletePresetAsync(new DeletePresetRequest { PresetId = preset.Id });
            }
        }
    }

    [WindowsFact]
    public async Task StopDurationScheduleEndsItsRunningSessionAutomatically()
    {
        var (_, presets, sessions, _, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        {
            var scheduler = new SchedulerService.SchedulerServiceClient(channel);
            var instanceId = Guid.NewGuid();
            var module = new Axorith.Contracts.ConfiguredModule
            {
                ModuleId = factory.AppBlockerModuleId.ToString(),
                InstanceId = instanceId.ToString()
            };
            module.Settings.Add("Mode", "BlockList");
            module.Settings.Add("Categories", "[]");
            module.Settings.Add("CustomProcessList", $"axorith-no-process-{Guid.NewGuid():N}");
            var preset = await presets.CreatePresetAsync(new CreatePresetRequest
            {
                Preset = new Preset { Name = $"AutoStop-{Guid.NewGuid():N}", Modules = { module } }
            });
            var schedule = await CreateScheduleAsync(scheduler, preset.Id, "Stop after two seconds",
                ScheduleType.StopDuration, value => value.AutoStopDurationSeconds = 2);
            Assert.True(schedule.IsEnabled);
            var started = false;

            try
            {
                var response = await sessions.StartSessionAsync(new StartSessionRequest { PresetId = preset.Id });
                Assert.True(response.Success, response.Message);
                started = true;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (stopwatch.Elapsed < TimeSpan.FromSeconds(8))
                {
                    var state = await sessions.GetSessionStateAsync(new GetSessionStateRequest());
                    if (!state.IsActive)
                    {
                        started = false;
                        break;
                    }

                    await Task.Delay(100);
                }

                Assert.False(started, "The real stop-duration schedule did not end its session.");
            }
            finally
            {
                if (started)
                    await sessions.StopSessionAsync(new StopSessionRequest());
                await scheduler.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = schedule.Id });
                await presets.DeletePresetAsync(new DeletePresetRequest { PresetId = preset.Id });
            }
        }
    }

    [Fact]
    public async Task HostToastIsStreamedAndMappedToTheConnectedClient()
    {
        var (_, _, _, _, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var notifications = new GrpcNotificationApi(
                new NotificationService.NotificationServiceClient(channel));
            await using var stream = notifications.StreamNotificationsAsync(timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            var notificationTask = stream.MoveNextAsync().AsTask();
            var broadcaster = factory.Services.GetRequiredService<NotificationBroadcaster>();
            for (var attempt = 0; attempt < 100 && !broadcaster.HasSubscribers; attempt++)
                await Task.Delay(10, timeout.Token);
            Assert.True(broadcaster.HasSubscribers);

            factory.Services.GetRequiredService<INotifier>()
                .ShowToast("Session saved", Axorith.Sdk.Services.NotificationType.Success);

            Assert.True(await notificationTask.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal("Session saved", stream.Current.Message);
            Assert.Equal(Axorith.Sdk.Services.NotificationType.Success, stream.Current.Type);
            Assert.Equal("Axorith", stream.Current.Source);
        }
    }

    private static async Task<Axorith.Contracts.SettingUpdate> ReadSettingUpdateAsync(
        IAsyncStreamReader<Axorith.Contracts.SettingUpdate> stream, string settingKey, string value,
        CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            var update = stream.Current;
            if (update.SettingKey == settingKey && update.ValueCase == Axorith.Contracts.SettingUpdate.ValueOneofCase.StringValue &&
                update.StringValue == value)
            {
                return update;
            }
        }

        throw new InvalidOperationException($"No update was streamed for setting '{settingKey}'.");
    }

    private async Task<Preset> CreateAppBlockerPresetAsync(PresetsService.PresetsServiceClient presets, string name)
    {
        var module = new Axorith.Contracts.ConfiguredModule
        {
            ModuleId = factory.AppBlockerModuleId.ToString(),
            InstanceId = Guid.NewGuid().ToString()
        };
        module.Settings.Add("Mode", "BlockList");
        module.Settings.Add("Categories", "[]");
        module.Settings.Add("CustomProcessList", $"axorith-no-process-{Guid.NewGuid():N}");
        var result = await presets.CreatePresetAsync(new CreatePresetRequest
        {
            Preset = new Preset { Name = $"{name}-{Guid.NewGuid():N}", Modules = { module } }
        });
        return result;
    }

    private async Task AssertScheduleStartsPresetAsync(string presetName, string scheduleName, ScheduleType type,
        System.Action<Axorith.Contracts.Schedule> configure)
    {
        var (_, presets, sessions, _, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        {
            var scheduler = new SchedulerService.SchedulerServiceClient(channel);
            var preset = await CreateAppBlockerPresetAsync(presets, presetName);
            string? scheduleId = null;

            try
            {
                var schedule = await CreateScheduleAsync(scheduler, preset.Id, scheduleName, type, configure);
                scheduleId = schedule.Id;

                var state = await WaitForSessionStateAsync(sessions,
                    value => value.IsActive && value.PresetId == preset.Id, TimeSpan.FromSeconds(10));
                Assert.True(state.IsActive);
                Assert.Equal(preset.Id, state.PresetId);
            }
            finally
            {
                await StopSessionForPresetIfRunningAsync(sessions, preset.Id);
                if (scheduleId is not null)
                    await scheduler.DeleteScheduleAsync(new DeleteScheduleRequest { ScheduleId = scheduleId });
                await presets.DeletePresetAsync(new DeletePresetRequest { PresetId = preset.Id });
            }
        }
    }

    private static async Task<Axorith.Contracts.Schedule> CreateScheduleAsync(
        SchedulerService.SchedulerServiceClient scheduler, string presetId, string name, ScheduleType type,
        System.Action<Axorith.Contracts.Schedule>? configure = null)
    {
        var schedule = new Axorith.Contracts.Schedule
        {
            PresetId = presetId,
            Name = name,
            IsEnabled = true,
            Type = (int)type
        };
        configure?.Invoke(schedule);
        return await scheduler.CreateScheduleAsync(new CreateScheduleRequest { Schedule = schedule });
    }

    private static async Task<SessionState> WaitForSessionStateAsync(
        SessionsService.SessionsServiceClient sessions, Func<SessionState, bool> condition, TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        SessionState state;
        do
        {
            state = await sessions.GetSessionStateAsync(new GetSessionStateRequest());
            if (condition(state))
                return state;

            await Task.Delay(100);
        } while (stopwatch.Elapsed < timeout);

        return state;
    }

    private static async Task StopSessionForPresetIfRunningAsync(
        SessionsService.SessionsServiceClient sessions, string presetId)
    {
        var state = await sessions.GetSessionStateAsync(new GetSessionStateRequest());
        if (state.IsActive && state.PresetId == presetId)
            await sessions.StopSessionAsync(new StopSessionRequest());
    }

    private async Task<Axorith.Contracts.ModuleDefinition> GetInstalledSiteBlockerAsync(
        ModulesService.ModulesServiceClient modules)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                var response = await modules.ListModulesAsync(new ListModulesRequest());
                var found = response.Modules.FirstOrDefault(item =>
                    item.Id == factory.SiteBlockerModuleId.ToString());
                if (found is not null)
                    return found;
            }
            catch (RpcException)
            {
                // The host initializes its module registry in the background during startup.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The real Site Blocker module was not discovered by the host.");
    }

    [Fact]
    public async Task Presets_GetPreset_WithInvalidId_ShouldReturnRpcInvalidArgument()
    {
        var (_, presets, _, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var act = async () =>
                await presets.GetPresetAsync(new GetPresetRequest
                {
                    PresetId = "invalid-guid"
                });

            var exception = await Assert.ThrowsAsync<RpcException>(act);
            exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        }
    }

    [Fact]
    public async Task Sessions_GetSessionState_WhenNoActiveSession_ShouldReturnInactive()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var state = await sessions.GetSessionStateAsync(new GetSessionStateRequest());

            state.Should().NotBeNull();
            state.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Sessions_StartSession_WithInvalidPresetId_ShouldReturnFailure()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var response = await sessions.StartSessionAsync(new StartSessionRequest
            {
                PresetId = "invalid-guid"
            });

                response.Success.Should().BeFalse();
            response.Message.Should().Contain("Invalid preset ID");
        }
    }

    [Fact]
    public async Task Sessions_StartSession_WithUnknownPreset_ShouldReturnFailure()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();

        using (channel)
        {
            var presetId = Guid.NewGuid().ToString();

            var response = await sessions.StartSessionAsync(new StartSessionRequest
            {
                PresetId = presetId
            });

                response.Success.Should().BeFalse();
            response.Message.Should().Contain("Preset not found");
        }
    }

    [Fact]
    public async Task EmergencyUnlockStream_ReturnsWhenHostRejectsHold()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        using (var api = new GrpcSessionsApi(sessions, Policy.Handle<Exception>().RetryAsync(0),
                   NullLogger.Instance))
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await using var progress = api.HoldEmergencyUnlockAsync(HeldSignals(timeout.Token), timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            Assert.True(await progress.MoveNextAsync());
            Assert.Contains("no committed session", progress.Current.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(await progress.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        }
    }

    private static async IAsyncEnumerable<bool> HeldSignals(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return true;
            await Task.Delay(100, cancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task FrontendInteractionsAndSessionEditorLayoutStayConsistent()
    {
        var (_, _, sessions, _, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        using (var api = new GrpcSessionsApi(sessions, Policy.Handle<Exception>().RetryAsync(0),
                   NullLogger.Instance))
        using (var services = new ServiceCollection().BuildServiceProvider())
        using (var viewModel = new MainViewModel(null!, null!, api, null!, services))
        {
            using var editorViewModel = new SessionEditorViewModel(null!, null!, null!, null!, null!, services);
            Dispatcher.UIThread.RunJobs();
            await editorViewModel.InitializationTask;
            var editorView = new SessionEditorView { DataContext = editorViewModel };
            var focusCard = editorView.FindControl<Border>("FocusCommitmentCard")!;
            Assert.Equal(40, focusCard.Margin.Top);
            focusCard.Measure(new Size(800, 1200));
            Assert.Equal(500, focusCard.DesiredSize.Width);
            Assert.False(editorView.FindControl<StackPanel>("ScheduleLockPanel")!.IsVisible);
            Assert.Null(editorView.FindControl<StackPanel>("CommittedEndConditionPanel"));
            Assert.Null(editorView.FindControl<StackPanel>("CommittedAfterEndPanel"));
            Assert.Empty(editorViewModel.StopTriggers);
            Assert.Empty(editorViewModel.ThenTriggers);
            Assert.Equal("Stop On...", editorView.FindControl<TextBlock>("StopOnHeading")!.Text);
            Assert.Equal("Then...", editorView.FindControl<TextBlock>("ThenHeading")!.Text);
            Assert.NotNull(editorView.FindControl<Button>("AddStopTriggerButton"));
            Assert.NotNull(editorView.FindControl<Button>("AddThenTriggerButton"));

            var thenCard = editorView.FindControl<Border>("ThenCard")!;
            var thenAction = new ThenActionTriggerViewModel(editorViewModel,
                Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace);
            editorViewModel.ThenTriggers.Add(thenAction);
            var thenItems = thenCard.GetVisualDescendants().OfType<ItemsControl>().Single();
            var thenActionCard = (Border)thenItems.ItemTemplate!.Build(thenAction)!;
            thenActionCard.DataContext = thenAction;
            var thenCardContent = thenCard.Child;
            thenCard.Child = thenActionCard;
            var editorWindow = new Window { Content = editorView, Width = 800, Height = 800 };
            try
            {
                editorWindow.Show();
                Dispatcher.UIThread.RunJobs();
                thenCard.Measure(new Size(800, 1200));
                thenCard.Arrange(new Rect(0, 0, 800, 1200));
                Assert.Equal(Color.Parse("#111"), ((ISolidColorBrush)thenActionCard.Background!).Color);
                Assert.Equal(Color.Parse("#FF6B6B"), ((ISolidColorBrush)thenActionCard.BorderBrush!).Color);
                Assert.Single(thenCard.GetVisualDescendants().OfType<TextBlock>(),
                    label => label is { IsVisible: true, Text: "No other Workspaces are available." });
                Assert.DoesNotContain(thenCard.GetVisualDescendants().OfType<TextBlock>(),
                    label => label is { IsVisible: true, Text: "Choose a Workspace" });
            }
            finally
            {
                editorWindow.Close();
                thenCard.Child = thenCardContent;
            }
            editorViewModel.ThenTriggers.Clear();

            editorViewModel.Name = "Focus commitment test";
            editorViewModel.FocusCommitmentModeIndex = (int)Axorith.Core.Models.FocusCommitmentMode.Strict;
            Dispatcher.UIThread.RunJobs();
            focusCard.Measure(new Size(800, 1200));
            Assert.Equal(500, focusCard.DesiredSize.Width);
            Assert.True(editorViewModel.HasValidationErrors);
            Assert.False(editorViewModel.SaveAndCloseCommand.CanExecute(null));
            Assert.Contains("Add one Stop Trigger", editorViewModel.FocusCommitmentError ?? string.Empty);
            Assert.Contains("error", focusCard.Classes);
            var focusCommitmentErrorText = editorView.FindControl<TextBlock>("FocusCommitmentErrorText")!;
            Assert.True(focusCommitmentErrorText.IsVisible);
            Assert.Contains("Add one Stop Trigger", focusCommitmentErrorText.Text ?? string.Empty);

            var validateCommitment = typeof(SessionEditorViewModel).GetMethod("ValidateFocusCommitment",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.False((bool)validateCommitment.Invoke(editorViewModel, [false])!);

            using (editorViewModel.AddStopAfterDurationTriggerCommand.Execute().Subscribe())
            {
                Dispatcher.UIThread.RunJobs();
            }
            var durationStop = editorViewModel.StopTriggers.OfType<StopAfterDurationTriggerViewModel>().Single();
            durationStop.DurationHours = 0;
            durationStop.DurationMinutes = 45;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(editorViewModel.FocusCommitmentError);
            Assert.False(editorViewModel.HasValidationErrors);
            Assert.True(editorViewModel.SaveAndCloseCommand.CanExecute(null));
            Assert.True((bool)validateCommitment.Invoke(editorViewModel, [true])!);
            var options = (Axorith.Core.Models.FocusCommitmentOptions)typeof(SessionEditorViewModel)
                .GetField("_focusCommitment", System.Reflection.BindingFlags.Instance |
                                                System.Reflection.BindingFlags.NonPublic)!
                .GetValue(editorViewModel)!;
            Assert.Equal(Axorith.Core.Models.FocusEndCondition.Duration, options.EndCondition);
            Assert.Equal(TimeSpan.FromMinutes(45), options.Duration);
            Assert.Equal(Axorith.Core.Models.AfterEndBehavior.DoNothing, options.AfterEnd);

            editorViewModel.StopTriggers.Clear();
            editorViewModel.StopTriggers.Add(new StopAtTimeTriggerViewModel
            {
                Time = new TimeSpan(18, 30, 0),
                RunOnMonday = true,
                RunOnTuesday = false,
                RunOnWednesday = false,
                RunOnThursday = false,
                RunOnFriday = true,
                RunOnSaturday = false,
                RunOnSunday = false
            });
            var nextPresetId = Guid.NewGuid();
            editorViewModel.ThenTriggers.Add(new ThenActionTriggerViewModel(editorViewModel,
                Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace) { NextPresetId = nextPresetId });
            Assert.True((bool)validateCommitment.Invoke(editorViewModel, [true])!);
            Assert.Equal(Axorith.Core.Models.FocusEndCondition.EndAt, options.EndCondition);
            Assert.Equal(new TimeOnly(18, 30), options.EndAtLocalTime);
            Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Friday }, options.EndAtDaysOfWeek);
            Assert.Equal(Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace, options.AfterEnd);
            Assert.Equal(nextPresetId, options.NextWorkspaceId);

            editorViewModel.ThenTriggers.Clear();
            editorViewModel.ThenTriggers.Add(new ThenActionTriggerViewModel(editorViewModel,
                Axorith.Core.Models.AfterEndBehavior.ShutDownPc));
            Assert.True((bool)validateCommitment.Invoke(editorViewModel, [true])!);
            Assert.Equal(Axorith.Core.Models.AfterEndBehavior.ShutDownPc, options.AfterEnd);
            Assert.Null(options.NextWorkspaceId);

            foreach (var (behavior, title) in new[]
                     {
                         (Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace, "Start next Workspace"),
                         (Axorith.Core.Models.AfterEndBehavior.LockPc, "Lock PC"),
                         (Axorith.Core.Models.AfterEndBehavior.Sleep, "Sleep"),
                         (Axorith.Core.Models.AfterEndBehavior.SignOut, "Sign out"),
                         (Axorith.Core.Models.AfterEndBehavior.ShutDownPc, "Shut down PC")
                     })
            {
                editorViewModel.ThenTriggers.Clear();
                var action = new ThenActionTriggerViewModel(editorViewModel, behavior);
                editorViewModel.ThenTriggers.Add(action);
                Assert.Equal(title, action.Title);
                if (behavior == Axorith.Core.Models.AfterEndBehavior.StartNextWorkspace)
                {
                    action.NextPresetId = nextPresetId;
                }
                Assert.True((bool)validateCommitment.Invoke(editorViewModel, [true])!);
                Assert.Equal(behavior, options.AfterEnd);
            }

            var resolveEndAt = typeof(Axorith.Core.Services.SessionManager)
                .GetMethod("ResolveEndAt",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
                    [typeof(TimeOnly), typeof(IReadOnlyCollection<DayOfWeek>), typeof(DateTimeOffset),
                        typeof(TimeZoneInfo)])!;
            var testTimeZone = TimeZoneInfo.CreateCustomTimeZone("Test UTC+03", TimeSpan.FromHours(3),
                "Test UTC+03", "Test UTC+03");
            var resolvedEnd = (DateTimeOffset)resolveEndAt.Invoke(null,
                [options.EndAtLocalTime!.Value, options.EndAtDaysOfWeek,
                    new DateTimeOffset(2024, 1, 1, 19, 0, 0, TimeSpan.FromHours(3)), testTimeZone])!;
            Assert.Equal(new DateTimeOffset(2024, 1, 5, 15, 30, 0, TimeSpan.Zero), resolvedEnd);

            editorViewModel.Triggers.Add(new ScheduleTriggerViewModel());
            Assert.True(editorView.FindControl<StackPanel>("ScheduleLockPanel")!.IsVisible);
            editorViewModel.Triggers.Clear();
            Assert.False(editorView.FindControl<StackPanel>("ScheduleLockPanel")!.IsVisible);

            var view = new MainView { DataContext = viewModel };
            var mainContent = Assert.IsType<Grid>(view.Content).Children.OfType<Grid>()
                .Single(grid => Grid.GetColumn(grid) == 1);
            var homeContent = view.FindControl<ScrollViewer>("HomeScrollViewer")!;
            Assert.True(viewModel.CreateSessionCommand.CanExecute(null));
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, true);

            Assert.False(viewModel.CreateSessionCommand.CanExecute(null));
            Assert.False(viewModel.StartSelectedCommand.CanExecute(null));
            Assert.False(viewModel.OpenSettingsCommand.CanExecute(null));
            Assert.False(mainContent.IsEnabled);

            view.Measure(new Size(320, 300));
            Assert.True(view.FindControl<Border>("StartDialogCard")!.DesiredSize.Width <= 320);
            Assert.True(view.FindControl<ScrollViewer>("StartDialogScrollViewer")!.DesiredSize.Height <= 300);
            Assert.Equal(KeyboardNavigationMode.Cycle,
                KeyboardNavigation.GetTabNavigation(view.FindControl<StackPanel>("StartDialogContent")!));

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, false);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEmergencyUnlockOpen))!
                .SetValue(viewModel, true);
            Assert.False(mainContent.IsEnabled);
            Assert.False(viewModel.CreateSessionCommand.CanExecute(null));
            view.Measure(new Size(320, 300));
            Assert.True(view.FindControl<Border>("EmergencyDialogCard")!.DesiredSize.Width <= 320);
            Assert.Equal(KeyboardNavigationMode.Cycle,
                KeyboardNavigation.GetTabNavigation(view.FindControl<StackPanel>("EmergencyDialogContent")!));

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEmergencyUnlockOpen))!
                .SetValue(viewModel, false);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsSessionActive))!
                .SetValue(viewModel, true);
            Assert.Contains(homeContent.GetLogicalDescendants().OfType<TextBlock>(),
                text => text.Text == viewModel.ActiveWorkspaceName && text.IsVisible);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, true);
            Assert.Equal(720, view.FindControl<Border>("StartDialogCard")!.MaxWidth);
            Assert.Equal(HorizontalAlignment.Stretch,
                view.FindControl<Border>("StartDialogCard")!.HorizontalAlignment);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, false);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEmergencyUnlockOpen))!
                .SetValue(viewModel, true);

            using var preset = new SessionPresetViewModel(new SessionPreset { Name = "Deep Work" },
                [], null!, services);
            var card = view.FindControl<ListBox>("PresetsListBox")!.ItemTemplate!.Build(preset)!;
            var actions = card.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "PresetActionsButton");
            Assert.Equal(1, actions.Opacity);

            var editor = new SessionEditorView();
            foreach (var templateName in new[] { "FilePickerSettingTemplate", "DirectoryPickerSettingTemplate" })
            {
                var picker = ((IDataTemplate)editor.Resources[templateName]!).Build(null)!;
                var history = picker.GetVisualDescendants().OfType<ComboBox>().Single();
                var historyItem = history.ItemTemplate!.Build("Recent path")!;
                var remove = historyItem.GetVisualDescendants().OfType<Button>().Single();
                Assert.Equal(1, remove.Opacity);
                Assert.True(remove.IsHitTestVisible);
            }

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.ActiveProtectionStatus))!
                .SetValue(viewModel, "Protection failed");
            var protectionStatus = homeContent.GetLogicalDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "Protection failed");
            Assert.Equal(Brushes.IndianRed, protectionStatus.Foreground);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.ActiveProtectionStatus))!
                .SetValue(viewModel, "Protection active");
            Assert.Equal(Brushes.LightGreen, protectionStatus.Foreground);
            Assert.False(protectionStatus.IsVisible);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.ActiveProtectionStatus))!
                .SetValue(viewModel, "Protection inactive");
            Assert.Equal(Brushes.Gray, protectionStatus.Foreground);
            Assert.False(protectionStatus.IsVisible);

            var window = new Window { Content = view, Width = 900, Height = 700 };
            try
            {
                typeof(MainViewModel).GetProperty(nameof(MainViewModel.ActiveFocusCommitmentMode))!
                    .SetValue(viewModel, Axorith.Core.Models.FocusCommitmentMode.Locked);
                typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsSessionActive))!
                    .SetValue(viewModel, true);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(view.FindControl<Button>("EmergencyUnlockHoldButton"),
                    window.FocusManager?.GetFocusedElement());
                var holdButton = view.FindControl<Button>("EmergencyUnlockHoldButton")!;
                var pointer = new Pointer(42, PointerType.Mouse, true);
                var pressed = new PointerPressedEventArgs(holdButton, pointer, window, new Point(1, 1), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton,
                        PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1)
                {
                    RoutedEvent = InputElement.PointerPressedEvent
                };
                holdButton.RaiseEvent(pressed);
                Assert.True(pressed.Handled);
                Assert.Same(holdButton, pointer.Captured);
                var holdField = typeof(MainViewModel).GetField("_emergencyUnlockCts",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Assert.NotNull(holdField.GetValue(viewModel));

                var released = new PointerReleasedEventArgs(holdButton, pointer, window, new Point(1, 1), 1,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                    KeyModifiers.None, MouseButton.Left)
                {
                    RoutedEvent = InputElement.PointerReleasedEvent
                };
                holdButton.RaiseEvent(released);
                Assert.True(released.Handled);
                Assert.Null(pointer.Captured);
                Assert.Null(holdField.GetValue(viewModel));
            }
            finally
            {
                window.Close();
            }

            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsEmergencyUnlockOpen))!
                .SetValue(viewModel, false);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsStartConfirmationOpen))!
                .SetValue(viewModel, true);
            var confirmation = new MainView { DataContext = viewModel };
            var confirmationWindow = new Window { Content = confirmation, Width = 900, Height = 700 };
            try
            {
                confirmationWindow.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(confirmation.FindControl<Button>("StartConfirmationCancelButton"),
                    confirmationWindow.FocusManager?.GetFocusedElement());
                Assert.Equal(720, confirmation.FindControl<Border>("StartDialogCard")!.MaxWidth);
            }
            finally
            {
                confirmationWindow.Close();
            }
        }
    }

    public sealed class TestApplication : Application;

    public sealed class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "AppBlocker process monitoring requires Windows.";
        }
    }
}
