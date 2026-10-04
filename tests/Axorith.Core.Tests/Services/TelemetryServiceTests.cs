using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Core.Telemetry;
using Axorith.Core.Tests.Helpers;
using Axorith.Telemetry;
using Axorith.Shared.Exceptions;

namespace Axorith.Core.Tests.Services;

public sealed class TelemetryServiceTests
{
    [Fact]
    public async Task FlushAsync_SendsQueuedEventsAndKeepsWhitelistedProperties()
    {
        await using var server = new PostHogTestServer();
        var distinctId = Guid.NewGuid().ToString("D");
        await using var telemetry = CreateTelemetry(server, "Axorith.Client", distinctId);

        var installAttemptId = Guid.NewGuid();
        telemetry.TrackEvent("InstallationConfirmed", new Dictionary<string, object?>
        {
            ["installAttemptId"] = installAttemptId,
            ["installMode"] = "update",
            ["currentVersion"] = "1.2.3",
            ["previousVersion"] = "1.2.2",
            ["privateValue"] = "must be removed"
        });

        using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await telemetry.FlushAsync(flushCts.Token);

        var sentEvent = ReadEvents(server)
            .Single(item => item.GetProperty("event").GetString() == "InstallationConfirmed");
        var properties = sentEvent.GetProperty("properties");

        Assert.Equal(distinctId, sentEvent.GetProperty("distinct_id").GetString());
        Assert.Equal(installAttemptId.ToString("D"), properties.GetProperty("installAttemptId").GetString());
        Assert.Equal("update", properties.GetProperty("installMode").GetString());
        Assert.Equal("1.2.3", properties.GetProperty("currentVersion").GetString());
        Assert.Equal("1.2.2", properties.GetProperty("previousVersion").GetString());
        Assert.False(properties.TryGetProperty("privateValue", out _));
    }

    [Fact]
    public async Task ProductAnalytics_ProjectsConfigurationAndDropsSensitiveValues()
    {
        Assert.Equal("session_already_running",
            ProductAnalyticsProperties.FailureReason(new SessionException("A session is already running.")));
        Assert.Equal("protection_unavailable",
            ProductAnalyticsProperties.FailureReason(new SessionException("Windows protection is unavailable.")));

        await using var server = new PostHogTestServer();
        await using var telemetry = CreateTelemetry(server, "Axorith.Host");

        var presetId = Guid.NewGuid();
        var nextPresetId = Guid.NewGuid();
        var appBlockerId = Guid.NewGuid();
        var siteBlockerId = Guid.NewGuid();
        var launcherId = Guid.NewGuid();
        var homeAssistantId = Guid.NewGuid();
        var preset = new SessionPreset(presetId)
        {
            Name = "private preset name",
            Version = 3,
            FocusCommitment = new FocusCommitmentOptions
            {
                Mode = FocusCommitmentMode.Strict,
                EndCondition = FocusEndCondition.EndAt,
                EndAtLocalTime = new TimeOnly(21, 30),
                EndAtDaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Friday],
                BreakCount = 2,
                BreakDuration = TimeSpan.FromMinutes(10),
                AfterEnd = AfterEndBehavior.StartNextWorkspace,
                NextWorkspaceId = nextPresetId,
                ScheduleLockMinutes = 15
            },
            Modules =
            [
                new ConfiguredModule
                {
                    ModuleId = appBlockerId,
                    InstanceId = Guid.NewGuid(),
                    CustomName = "private module name",
                    StartDelay = TimeSpan.FromSeconds(3),
                    Settings = new Dictionary<string, string>
                    {
                        ["Mode"] = "BlockList",
                        ["Categories"] = "Gaming|Work|Not a category",
                        ["CustomProcessList"] = "private-process\r\nsecret-app"
                    }
                },
                new ConfiguredModule
                {
                    ModuleId = siteBlockerId,
                    InstanceId = Guid.NewGuid(),
                    Settings = new Dictionary<string, string>
                    {
                        ["Mode"] = "AllowList",
                        ["Categories"] = "Forums|Adult|Not a category",
                        ["CustomSites"] = "private.example\nsecret.example"
                    }
                },
                new ConfiguredModule
                {
                    ModuleId = launcherId,
                    InstanceId = Guid.NewGuid(),
                    Settings = new Dictionary<string, string>
                    {
                        ["ApplicationPath"] = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe",
                        ["ProcessMode"] = "LaunchOrAttach",
                        ["WindowState"] = "Maximized",
                        ["UseCustomSize"] = "true",
                        ["WindowWidth"] = "1600",
                        ["WindowHeight"] = "900",
                        ["MoveToMonitor"] = "true",
                        ["TargetMonitor"] = "2",
                        ["LifecycleMode"] = "TerminateForce",
                        ["BringToForeground"] = "true",
                        ["UseCustomWorkingDirectory"] = "true",
                        ["WorkingDirectory"] = @"C:\private\working-directory",
                        ["EnableWebSocket"] = "true",
                        ["WebSocketPort"] = "4455",
                        ["WebSocketPassword"] = "never-send-this-password",
                        ["SessionStartAction"] = "start_recording",
                        ["SessionEndAction"] = "stop_recording"
                    }
                },
                new ConfiguredModule
                {
                    ModuleId = homeAssistantId,
                    InstanceId = Guid.NewGuid(),
                    Settings = new Dictionary<string, string>
                    {
                        ["BaseUrl"] = "https://homeassistant.private.example",
                        ["AccessToken"] = "never-send-this-token",
                        ["StartEntityId"] = "light.private_room",
                        ["EndEntityId"] = "scene.private_scene",
                        ["Instructions"] = "private setup notes"
                    }
                }
            ]
        };
        var moduleNames = new Dictionary<Guid, string>
        {
            [appBlockerId] = "App Blocker",
            [siteBlockerId] = "Site Blocker",
            [launcherId] = "Application Launcher",
            [homeAssistantId] = "Home Assistant"
        };

        telemetry.TrackEvent("PresetCreated", ProductAnalyticsProperties.Preset(preset, moduleNames));
        var durationPreset = new SessionPreset(Guid.NewGuid())
        {
            FocusCommitment = new FocusCommitmentOptions
            {
                EndCondition = FocusEndCondition.Duration,
                Duration = TimeSpan.FromMinutes(25)
            }
        };
        telemetry.TrackEvent("PresetUpdated", ProductAnalyticsProperties.Preset(durationPreset, moduleNames));
        telemetry.TrackEvent("ModuleConfigurationSaved", ProductAnalyticsProperties.ModuleConfigurationSaved(
            preset, preset.Modules[0], "App Blocker", "create"));
        foreach (var eventName in new[] { "SessionStarted", "SessionStopped" })
        {
            var properties = ProductAnalyticsProperties.FocusCommitment(preset.FocusCommitment);
            properties["presetId"] = preset.Id;
            properties["sessionInstanceId"] = Guid.NewGuid();
            if (eventName == "SessionStopped")
            {
                properties["durationMs"] = 600_000L;
                properties["stopReason"] = "natural_completion";
                properties["completedAsPlanned"] = true;
            }
            telemetry.TrackEvent(eventName, properties);
        }
        var schedule = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = presetId,
            Name = "private schedule name",
            IsEnabled = true,
            Type = ScheduleType.Recurring,
            RecurringTime = TimeSpan.FromHours(9) + TimeSpan.FromMinutes(35),
            DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday],
            Use24HourFormat = false,
            NextPresetId = nextPresetId
        };
        telemetry.TrackEvent("ScheduleChanged", ProductAnalyticsProperties.Schedule(schedule, "create"));
        var durationSchedule = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = presetId,
            IsEnabled = true,
            Type = ScheduleType.StopDuration,
            AutoStopDuration = TimeSpan.FromMinutes(12),
            NextPresetId = nextPresetId
        };
        telemetry.TrackEvent("ScheduleTriggered", ProductAnalyticsProperties.ScheduleTriggered(
            durationSchedule, "stop", "completed"));
        var recurringStop = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = presetId,
            Type = ScheduleType.StopRecurring,
            RecurringTime = TimeSpan.FromHours(18) + TimeSpan.FromMinutes(15),
            DaysOfWeek = [DayOfWeek.Tuesday]
        };
        telemetry.TrackEvent("RecurringStopScheduleChanged", ProductAnalyticsProperties.Schedule(recurringStop, "update"));
        var oneTimeSchedule = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = presetId,
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.Date.AddDays(2).AddHours(15)
        };
        telemetry.TrackEvent("OneTimeScheduleChanged", ProductAnalyticsProperties.Schedule(oneTimeSchedule, "create"));
        telemetry.TrackEvent("ModuleActionInvoked", new Dictionary<string, object?>
        {
            ["moduleName"] = "App Blocker",
            ["actionKey"] = ProductAnalyticsProperties.NormalizeActionKey("AddApp"),
            ["result"] = "success"
        });

        using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await telemetry.FlushAsync(flushCts.Token);

        var events = ReadEvents(server).ToDictionary(item => item.GetProperty("event").GetString()!, item => item.GetProperty("properties"));

        var presetProperties = events["PresetCreated"];
        Assert.Equal(presetId.ToString("D"), presetProperties.GetProperty("presetId").GetString());
        Assert.Equal(3, presetProperties.GetProperty("presetVersion").GetInt32());
        Assert.Equal("strict", presetProperties.GetProperty("commitmentMode").GetString());
        Assert.Equal("end_at", presetProperties.GetProperty("endConditionType").GetString());
        Assert.Equal(1290, presetProperties.GetProperty("endAtMinuteOfDay").GetInt32());
        Assert.Equal(new[] { "mon", "fri" }, presetProperties.GetProperty("endAtDaysOfWeek").EnumerateArray()
            .Select(day => day.GetString()).ToArray());
        Assert.True(presetProperties.GetProperty("hasNextWorkspace").GetBoolean());
        Assert.Equal(15, presetProperties.GetProperty("scheduleLockMinutes").GetInt32());
        var modules = presetProperties.GetProperty("modules").EnumerateArray().ToArray();
        var appBlocker = modules.Single(module => module.GetProperty("moduleName").GetString() == "App Blocker");
        Assert.Equal("block_list", appBlocker.GetProperty("blockingMode").GetString());
        Assert.Equal(new[] { "Gaming" }, appBlocker.GetProperty("categories").EnumerateArray()
            .Select(category => category.GetString()).ToArray());
        Assert.Equal(2, appBlocker.GetProperty("customProcessCount").GetInt32());
        Assert.Equal(3000, appBlocker.GetProperty("startDelayMs").GetInt32());
        Assert.False(appBlocker.TryGetProperty("CustomProcessList", out _));

        var siteBlocker = modules.Single(module => module.GetProperty("moduleName").GetString() == "Site Blocker");
        Assert.Equal("allow_list", siteBlocker.GetProperty("blockingMode").GetString());
        Assert.Equal(new[] { "Adult", "Forums" }, siteBlocker.GetProperty("categories").EnumerateArray()
            .Select(category => category.GetString()).ToArray());
        Assert.Equal(2, siteBlocker.GetProperty("customSiteCount").GetInt32());

        var launcher = modules.Single(module => module.GetProperty("moduleName").GetString() == "Application Launcher");
        Assert.Equal("obs", launcher.GetProperty("launcherAppType").GetString());
        Assert.Equal("launch_or_attach", launcher.GetProperty("processMode").GetString());
        Assert.True(launcher.GetProperty("webSocketPortIsDefault").GetBoolean());
        Assert.True(launcher.GetProperty("webSocketEnabled").GetBoolean());
        Assert.Equal("start_recording", launcher.GetProperty("sessionStartAction").GetString());
        Assert.Equal("stop_recording", launcher.GetProperty("sessionEndAction").GetString());
        Assert.True(launcher.GetProperty("useCustomWorkingDirectory").GetBoolean());
        Assert.True(launcher.GetProperty("hasWorkingDirectory").GetBoolean());
        Assert.False(launcher.TryGetProperty("ApplicationPath", out _));
        Assert.False(launcher.TryGetProperty("WebSocketPassword", out _));

        var homeAssistant = modules.Single(module => module.GetProperty("moduleName").GetString() == "Home Assistant");
        Assert.True(homeAssistant.GetProperty("hasAccessToken").GetBoolean());
        Assert.True(homeAssistant.GetProperty("usesHttps").GetBoolean());
        Assert.False(homeAssistant.TryGetProperty("BaseUrl", out _));
        Assert.False(homeAssistant.TryGetProperty("AccessToken", out _));
        Assert.False(homeAssistant.TryGetProperty("StartEntityId", out _));

        var scheduleProperties = events["ScheduleChanged"];
        Assert.Equal("recurring_start", scheduleProperties.GetProperty("scheduleType").GetString());
        Assert.False(scheduleProperties.GetProperty("hasNextPreset").GetBoolean());
        Assert.Equal(575, scheduleProperties.GetProperty("scheduledMinuteOfDay").GetInt32());
        Assert.Equal(2, scheduleProperties.GetProperty("daysCount").GetInt32());
        Assert.False(scheduleProperties.TryGetProperty("OneTimeDate", out _));
        var recurringStopProperties = events["RecurringStopScheduleChanged"];
        Assert.Equal("recurring_stop", recurringStopProperties.GetProperty("scheduleType").GetString());
        Assert.Equal(1095, recurringStopProperties.GetProperty("scheduledMinuteOfDay").GetInt32());
        Assert.Equal("tue", recurringStopProperties.GetProperty("daysOfWeek")[0].GetString());
        var oneTimeProperties = events["OneTimeScheduleChanged"];
        var oneTimeLocal = oneTimeSchedule.OneTimeDate!.Value.ToLocalTime();
        Assert.Equal(oneTimeLocal.Hour * 60 + oneTimeLocal.Minute, oneTimeProperties.GetProperty("oneTimeMinuteOfDay").GetInt32());
        var expectedOneTimeDay = oneTimeLocal.DayOfWeek switch
        {
            DayOfWeek.Monday => "mon",
            DayOfWeek.Tuesday => "tue",
            DayOfWeek.Wednesday => "wed",
            DayOfWeek.Thursday => "thu",
            DayOfWeek.Friday => "fri",
            DayOfWeek.Saturday => "sat",
            _ => "sun"
        };
        Assert.Equal(expectedOneTimeDay, oneTimeProperties.GetProperty("oneTimeDayOfWeek").GetString());
        Assert.True(oneTimeProperties.GetProperty("oneTimeLeadTimeMs").GetInt64() > 0);
        Assert.False(oneTimeProperties.TryGetProperty("OneTimeDate", out _));
        Assert.Equal("add_app", events["ModuleActionInvoked"].GetProperty("actionKey").GetString());
        Assert.Equal("create", events["ModuleConfigurationSaved"].GetProperty("presetChangeType").GetString());
        Assert.True(events["SessionStarted"].GetProperty("endAtConfigured").GetBoolean());
        Assert.Equal(15, events["SessionStarted"].GetProperty("scheduleLockMinutes").GetInt32());
        Assert.Equal("start_next_workspace", events["SessionStopped"].GetProperty("afterEndAction").GetString());
        Assert.Equal("duration_stop", events["ScheduleTriggered"].GetProperty("scheduleType").GetString());
        Assert.Equal(720_000, events["ScheduleTriggered"].GetProperty("autoStopDurationMs").GetInt32());
        Assert.True(events["ScheduleTriggered"].GetProperty("hasNextPreset").GetBoolean());
        Assert.Equal(nextPresetId.ToString("D"), events["ScheduleTriggered"].GetProperty("nextPresetId").GetString());
        Assert.Equal("duration", events["PresetUpdated"].GetProperty("endConditionType").GetString());
        Assert.Equal(1_500_000, events["PresetUpdated"].GetProperty("plannedDurationMs").GetInt32());

        var payloadText = string.Join("\n", server.Payloads);
        foreach (var sensitiveValue in new[]
                 {
                     "private-process", "secret-app", "private.example", "never-send-this-password",
                     "never-send-this-token", "homeassistant.private.example", "light.private_room",
                      "scene.private_scene", "private setup notes", "private preset name", "private schedule name",
                      "private module name", @"C:\private\working-directory",
                      @"C:\Program Files\obs-studio\bin\64bit\obs64.exe"
                 })
        {
            Assert.DoesNotContain(sensitiveValue, payloadText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LauncherAnalytics_UsesSafeTypesAndPresenceFlagsForEveryLauncher()
    {
        await using var server = new PostHogTestServer();
        await using var telemetry = CreateTelemetry(server, "Axorith.Host");

        var launcherId = Guid.NewGuid();
        ConfiguredModule Launcher(string path, params (string Key, string Value)[] values)
        {
            var settings = new Dictionary<string, string> { ["ApplicationPath"] = path };
            foreach (var (key, value) in values) settings[key] = value;
            return new ConfiguredModule
            {
                ModuleId = launcherId,
                InstanceId = Guid.NewGuid(),
                Settings = settings
            };
        }

        var modules = new[]
        {
            ProductAnalyticsProperties.Module(Launcher(@"C:\Chrome\chrome.exe",
                ("StartUrl", "https://private.example"), ("ProfileName", "private profile"),
                ("IncognitoMode", "true"), ("AdditionalArgs", "--private")), "Application Launcher"),
            ProductAnalyticsProperties.Module(Launcher(@"C:\JetBrains\rider64.exe",
                ("ProjectPath", @"C:\private\rider-project"), ("ApplicationArgs", "--private-rider")),
                "Application Launcher"),
            ProductAnalyticsProperties.Module(Launcher(@"C:\Spotify\Spotify.exe",
                ("EnablePlayback", "true"), ("DeviceSelectionMode", "SpecificName"),
                ("SpecificDeviceName", "private speaker"), ("PlaybackContext", "spotify:playlist:private-id"),
                ("CustomUrl", "https://private.example/spotify"), ("Volume", "83"),
                ("Shuffle", "true"), ("RepeatMode", "context")), "Application Launcher"),
            ProductAnalyticsProperties.Module(Launcher(@"C:\Steam\steam.exe", ("SelectedGame", "private-game-id")),
                "Application Launcher"),
            ProductAnalyticsProperties.Module(Launcher(@"C:\VSCode\Code.exe",
                ("ProjectPath", @"C:\private\vscode-project"), ("ApplicationArgs", "--private-vscode")),
                "Application Launcher"),
            ProductAnalyticsProperties.Module(Launcher(@"C:\Discord\Discord.exe"), "Application Launcher"),
            ProductAnalyticsProperties.Module(Launcher("custom-app", ("CustomPath", @"C:\private\custom-app.exe")),
                "Application Launcher")
        };

        telemetry.TrackEvent("PresetUpdated", new Dictionary<string, object?> { ["modules"] = modules });
        using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await telemetry.FlushAsync(flushCts.Token);

        var payloadText = string.Join("\n", server.Payloads);
        var presetEvent = ReadEvents(server).Single(item => item.GetProperty("event").GetString() == "PresetUpdated");
        var safeModules = presetEvent.GetProperty("properties").GetProperty("modules").EnumerateArray().ToArray();
        var browser = safeModules.Single(module => module.GetProperty("launcherAppType").GetString() == "browser");
        Assert.Equal("chromium", browser.GetProperty("browserFamily").GetString());
        Assert.True(browser.GetProperty("incognitoMode").GetBoolean());
        var ide = safeModules.Single(module => module.GetProperty("launcherAppType").GetString() == "jetbrains");
        Assert.Equal("rider", ide.GetProperty("ideFamily").GetString());
        Assert.True(ide.GetProperty("hasProjectPath").GetBoolean());
        Assert.True(ide.GetProperty("hasApplicationArgs").GetBoolean());
        var spotify = safeModules.Single(module => module.GetProperty("launcherAppType").GetString() == "spotify");
        Assert.Equal("playlist", spotify.GetProperty("playbackContextKind").GetString());
        Assert.Equal(83, spotify.GetProperty("volume").GetInt32());
        Assert.Equal("context", spotify.GetProperty("repeatMode").GetString());
        Assert.True(spotify.GetProperty("hasSpecificDeviceName").GetBoolean());
        Assert.True(safeModules.Single(module => module.GetProperty("launcherAppType").GetString() == "steam")
            .GetProperty("hasSelectedGame").GetBoolean());
        Assert.True(safeModules.Single(module => module.GetProperty("launcherAppType").GetString() == "vscode")
            .GetProperty("hasProjectPath").GetBoolean());
        Assert.Equal(1, safeModules.Count(module => module.GetProperty("launcherAppType").GetString() == "discord"));
        Assert.True(safeModules.Single(module => module.GetProperty("launcherAppType").GetString() == "custom")
            .GetProperty("isCustomApp").GetBoolean());

        foreach (var sensitiveValue in new[]
                 {
                     "private.example", "private profile", "--private", "private-rider", "private-playlist-id",
                     "private speaker", "private-game-id", @"C:\private\working-directory",
                     @"C:\private\rider-project", @"C:\private\vscode-project", "private-vscode",
                     @"C:\private\custom-app.exe"
                 })
        {
            Assert.DoesNotContain(sensitiveValue, payloadText, StringComparison.Ordinal);
        }
    }
    private static TelemetryService CreateTelemetry(PostHogTestServer server, string applicationName,
        string? distinctId = null) => new(new TelemetrySettings
    {
        Enabled = true,
        PostHogApiKey = "test-project-key",
        PostHogHost = server.Host,
        DistinctId = distinctId ?? Guid.NewGuid().ToString("D"),
        ApplicationName = applicationName,
        AppVersion = "1.2.3",
        OsVersion = "Windows 11",
        BatchSize = 100,
        FlushInterval = TimeSpan.FromHours(1)
    });

    private static IEnumerable<JsonElement> ReadEvents(PostHogTestServer server) => server.Payloads.SelectMany(body =>
    {
        using var payload = JsonDocument.Parse(body);
        return payload.RootElement.GetProperty("batch").EnumerateArray().Select(item => item.Clone()).ToArray();
    });

}
