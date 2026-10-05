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
        Assert.Equal("Axorith.Client", properties.GetProperty("application").GetString());
        Assert.False(properties.TryGetProperty("privateValue", out _));

        var identify = ReadEvents(server).Single(item => item.GetProperty("event").GetString() == "$identify");
        Assert.False(identify.GetProperty("properties").GetProperty("$set").TryGetProperty("application", out _));
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
        Assert.Equal(new[] { "private-process", "secret-app" }, appBlocker.GetProperty("customProcessNames")
            .EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(3000, appBlocker.GetProperty("startDelayMs").GetInt32());
        Assert.False(appBlocker.TryGetProperty("CustomProcessList", out _));

        var siteBlocker = modules.Single(module => module.GetProperty("moduleName").GetString() == "Site Blocker");
        Assert.Equal("allow_list", siteBlocker.GetProperty("blockingMode").GetString());
        Assert.Equal(new[] { "Adult", "Forums" }, siteBlocker.GetProperty("categories").EnumerateArray()
            .Select(category => category.GetString()).ToArray());
        Assert.Equal(2, siteBlocker.GetProperty("customSiteCount").GetInt32());
        Assert.Equal(new[] { "private.example", "secret.example" }, siteBlocker.GetProperty("customSiteDomains")
            .EnumerateArray().Select(value => value.GetString()).ToArray());

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
                     "never-send-this-password",
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
        Assert.Equal("private.example", browser.GetProperty("startUrlDomain").GetString());
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
                     "private profile", "--private", "private-rider", "private-playlist-id",
                     "private speaker", "private-game-id", @"C:\private\working-directory",
                     @"C:\private\rider-project", @"C:\private\vscode-project", "private-vscode",
                     @"C:\private\custom-app.exe"
                 })
        {
            Assert.DoesNotContain(sensitiveValue, payloadText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ModuleAnalytics_SendsOnlyNormalizedDetailsAndKeyedHashes()
    {
        await using var server = new PostHogTestServer();
        await using var telemetry = CreateTelemetry(server, "Axorith.Host");
        var secureStorage = new InMemorySecureStorage();

        ConfiguredModule Module(params (string Key, string Value)[] values)
        {
            var settings = values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            return new ConfiguredModule { ModuleId = Guid.NewGuid(), InstanceId = Guid.NewGuid(), Settings = settings };
        }

        var appBlocker = ProductAnalyticsProperties.Module(Module(
            ("CustomProcessList", "C:\\Private\\Discord.exe\nSlack.exe")), "App Blocker", secureStorage: secureStorage);
        var siteBlocker = ProductAnalyticsProperties.Module(Module(
            ("CustomSites", "https://reddit.com/r/cpp?q=private\nhttps://example.org/private")),
            "Site Blocker", secureStorage: secureStorage);
        var customLauncher = ProductAnalyticsProperties.Module(Module(
            ("ApplicationPath", "custom-app"), ("CustomPath", @"C:\Private\custom-editor.exe"),
            ("ProjectPath", @"C:\Private\Project"), ("UseCustomWorkingDirectory", "true"),
            ("WorkingDirectory", @"C:\Private\Work"), ("UseCustomSize", "false"),
            ("WindowWidth", "1280"), ("WindowHeight", "720"), ("MoveToMonitor", "true"),
            ("TargetMonitor", "2")), "Application Launcher", secureStorage: secureStorage);
        var browser = ProductAnalyticsProperties.Module(Module(
            ("ApplicationPath", @"C:\Program Files\Chrome\chrome.exe"),
            ("StartUrl", "https://forum.example.com/r/topic?q=private"), ("ProfileName", "private-profile")),
            "Application Launcher", secureStorage: secureStorage);
        var obs = ProductAnalyticsProperties.Module(Module(
            ("ApplicationPath", @"C:\OBS\obs64.exe"), ("EnableWebSocket", "true"),
            ("WebSocketPort", "5678"), ("WebSocketPassword", "private-obs-password")),
            "Application Launcher", secureStorage: secureStorage);
        var steam = ProductAnalyticsProperties.Module(Module(
            ("ApplicationPath", @"C:\Steam\steam.exe"), ("SelectedGame", "570")),
            "Application Launcher", secureStorage: secureStorage);
        var homeAssistant = ProductAnalyticsProperties.Module(Module(
            ("BaseUrl", "https://homeassistant.private.example"), ("AccessToken", "private-ha-token"),
            ("StartEntityId", "light.private_room"), ("EndEntityId", "scene.private_scene")),
            "Home Assistant", true, secureStorage);

        telemetry.TrackEvent("PresetCreated", new Dictionary<string, object?>
        {
            ["modules"] = new[] { appBlocker, siteBlocker, customLauncher, browser, obs, steam, homeAssistant }
        });
        await telemetry.FlushAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        var eventPayload = ReadEvents(server).Single(item => item.GetProperty("event").GetString() == "PresetCreated");
        var modules = eventPayload.GetProperty("properties").GetProperty("modules").EnumerateArray().ToArray();
        var safeAppBlocker = modules.Single(module => module.GetProperty("moduleName").GetString() == "App Blocker");
        Assert.Equal(new[] { "discord", "slack" }, safeAppBlocker.GetProperty("customProcessNames")
            .EnumerateArray().Select(value => value.GetString()).ToArray());
        var safeSiteBlocker = modules.Single(module => module.GetProperty("moduleName").GetString() == "Site Blocker");
        Assert.Equal(new[] { "example.org", "reddit.com" }, safeSiteBlocker.GetProperty("customSiteDomains")
            .EnumerateArray().Select(value => value.GetString()).ToArray());

        var safeCustomLauncher = modules.Single(module => module.TryGetProperty("customApplicationExecutable", out _));
        Assert.Equal("custom-editor", safeCustomLauncher.GetProperty("customApplicationExecutable").GetString());
        Assert.Equal(64, safeCustomLauncher.GetProperty("customApplicationPathHash").GetString()!.Length);
        Assert.Equal(64, safeCustomLauncher.GetProperty("projectPathHash").GetString()!.Length);
        Assert.Equal(64, safeCustomLauncher.GetProperty("workingDirectoryHash").GetString()!.Length);
        Assert.False(safeCustomLauncher.TryGetProperty("windowWidth", out _));
        Assert.False(safeCustomLauncher.TryGetProperty("windowHeight", out _));
        Assert.Equal(2, safeCustomLauncher.GetProperty("targetMonitorIndex").GetInt32());

        var safeBrowser = modules.Single(module => module.TryGetProperty("startUrlDomain", out _));
        Assert.Equal("forum.example.com", safeBrowser.GetProperty("startUrlDomain").GetString());
        Assert.Equal(64, safeBrowser.GetProperty("browserProfileHash").GetString()!.Length);
        var safeObs = modules.Single(module => module.TryGetProperty("webSocketEnabled", out _));
        Assert.Equal(5678, safeObs.GetProperty("webSocketPort").GetInt32());
        Assert.True(safeObs.GetProperty("hasWebSocketPassword").GetBoolean());
        var safeSteam = modules.Single(module => module.TryGetProperty("launcherAppType", out var appType) &&
                                                 appType.GetString() == "steam");
        Assert.Equal(570, safeSteam.GetProperty("selectedSteamAppId").GetInt32());
        var safeHomeAssistant = modules.Single(module => module.GetProperty("moduleName").GetString() == "Home Assistant");
        Assert.Equal("light", safeHomeAssistant.GetProperty("startEntityDomain").GetString());
        Assert.Equal("scene", safeHomeAssistant.GetProperty("endEntityDomain").GetString());
        Assert.True(safeHomeAssistant.GetProperty("hasAccessToken").GetBoolean());

        var payloadText = string.Join("\n", server.Payloads);
        foreach (var sensitiveValue in new[]
                 {
                     @"C:\Private\Discord.exe", "https://reddit.com/r/cpp?q=private", "forum.example.com/r/topic?q=private",
                     "private-profile", @"C:\Private\Project", @"C:\Private\Work", @"C:\Private\custom-editor.exe",
                     "private-obs-password", "private-ha-token", "light.private_room", "scene.private_scene"
                 })
        {
            Assert.DoesNotContain(sensitiveValue, payloadText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FocusCommitmentAndProductState_OmitUnusedBreaksAndSetCurrentCounts()
    {
        await using var server = new PostHogTestServer();
        await using var telemetry = CreateTelemetry(server, "Axorith.Host");
        var preset = new SessionPreset(Guid.NewGuid());
        var schedule = new SessionSchedule { Id = Guid.NewGuid(), PresetId = preset.Id, IsEnabled = false };
        telemetry.TrackEvent("$identify", new Dictionary<string, object?>
        {
            ["$set"] = ProductAnalyticsProperties.ProductState([preset], [schedule])
        });
        telemetry.TrackEvent("PresetCreated", ProductAnalyticsProperties.FocusCommitment(preset.FocusCommitment));
        telemetry.TrackEvent("OnboardingCompleted", new Dictionary<string, object?>
        {
            ["createdCount"] = 3,
            ["createdModuleTypes"] = new[] { "Developer", "Gamer", "Streamer" }
        });
        await telemetry.FlushAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        var events = ReadEvents(server).ToArray();
        var identifyProperties = events.Last(item => item.GetProperty("event").GetString() == "$identify")
            .GetProperty("properties").GetProperty("$set");
        Assert.Equal(1, identifyProperties.GetProperty("presetCount").GetInt32());
        Assert.Equal(1, identifyProperties.GetProperty("scheduleCount").GetInt32());
        Assert.Equal(0, identifyProperties.GetProperty("enabledScheduleCount").GetInt32());
        Assert.True(identifyProperties.GetProperty("hasScheduler").GetBoolean());
        Assert.False(identifyProperties.GetProperty("hasCommittedPreset").GetBoolean());
        Assert.False(events.Single(item => item.GetProperty("event").GetString() == "PresetCreated")
            .GetProperty("properties").TryGetProperty("breakDurationMs", out _));
        Assert.Equal(new[] { "Developer", "Gamer", "Streamer" }, events.Single(item =>
                item.GetProperty("event").GetString() == "OnboardingCompleted")
            .GetProperty("properties").GetProperty("createdModuleTypes").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
    }

    private sealed class InMemorySecureStorage : Axorith.Sdk.Services.ISecureStorageService
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public void StoreSecret(string key, string secret) => _values[key] = secret;
        public string? RetrieveSecret(string key) => _values.GetValueOrDefault(key);
        public void DeleteSecret(string key) => _values.Remove(key);
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
