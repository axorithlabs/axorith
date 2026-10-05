using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Axorith.Core.Models;
using Axorith.Sdk.Services;
using Axorith.Shared.Exceptions;
using Axorith.Shared.Platform;

namespace Axorith.Core.Telemetry;

public static partial class ProductAnalyticsProperties
{
    private const string PathHmacKey = "axorith:telemetry:path-hmac-key";
    private static readonly object PathHmacLock = new();
    private static readonly HashSet<string> AppBlockerCategories =
        ["Gaming", "Social", "Browsers", "Entertainment", "Productivity", "Email", "Development", "Design", "Office"];
    private static readonly HashSet<string> SiteBlockerCategories =
        ["Social", "Video", "Streaming", "Gaming", "News", "Shopping", "Music", "Work", "Adult", "Gambling", "Dating", "Forums"];

    public static Dictionary<string, object?> FocusCommitment(FocusCommitmentOptions options)
    {
        var hasNextWorkspace = options is { AfterEnd: AfterEndBehavior.StartNextWorkspace, NextWorkspaceId: not null };
        var endAtConfigured = options is { EndCondition: FocusEndCondition.EndAt, EndAtLocalTime: not null };
        return new Dictionary<string, object?>
        {
            ["commitmentMode"] = options.Mode switch
            {
                FocusCommitmentMode.Locked => "locked",
                FocusCommitmentMode.Strict => "strict",
                _ => "normal"
            },
            ["endConditionType"] = options.EndCondition switch
            {
                FocusEndCondition.Duration => "duration",
                FocusEndCondition.EndAt => "end_at",
                _ => "none"
            },
            ["plannedDurationMs"] = options is { EndCondition: FocusEndCondition.Duration, Duration: { } duration }
                ? (long)duration.TotalMilliseconds
                : null,
            ["endAtConfigured"] = endAtConfigured,
            ["endAtMinuteOfDay"] = endAtConfigured && options.EndAtLocalTime is { } endAt
                ? endAt.Hour * 60 + endAt.Minute
                : null,
            ["endAtDaysOfWeek"] = endAtConfigured
                ? DayKeys(options.EndAtDaysOfWeek)
                : Array.Empty<string>(),
            ["breakCount"] = options.BreakCount,
            ["breakDurationMs"] = options.BreakCount > 0 ? (long)options.BreakDuration.TotalMilliseconds : null,
            ["afterEndAction"] = options.AfterEnd switch
            {
                AfterEndBehavior.StartNextWorkspace => "start_next_workspace",
                AfterEndBehavior.LockPc => "lock_pc",
                AfterEndBehavior.Sleep => "sleep",
                AfterEndBehavior.SignOut => "sign_out",
                AfterEndBehavior.ShutDownPc => "shut_down_pc",
                _ => "do_nothing"
            },
            ["hasNextWorkspace"] = hasNextWorkspace,
            ["nextWorkspaceId"] = hasNextWorkspace ? options.NextWorkspaceId : null,
            ["scheduleLockMinutes"] = options.ScheduleLockMinutes
        };
    }

    public static Dictionary<string, object?> Preset(SessionPreset preset,
        IReadOnlyDictionary<Guid, string> moduleNames,
        ISecureStorageService? secureStorage = null)
    {
        var modules = preset.Modules.Select(module => Module(module,
            moduleNames.GetValueOrDefault(module.ModuleId, "custom"),
            HasHomeAssistantAccessToken(module, moduleNames.GetValueOrDefault(module.ModuleId, "custom"), secureStorage),
            secureStorage)).ToArray();
        var properties = FocusCommitment(preset.FocusCommitment);
        properties["presetId"] = preset.Id;
        properties["presetVersion"] = preset.Version;
        properties["moduleCount"] = modules.Length;
        properties["moduleIds"] = preset.Modules.Select(module => module.ModuleId.ToString()).ToArray();
        properties["moduleTypes"] = modules.Select(module => (string)module["moduleName"]!).Distinct().ToArray();
        properties["modules"] = modules;
        return properties;
    }

    public static Dictionary<string, object?> ModuleConfigurationSaved(SessionPreset preset,
        ConfiguredModule module, string moduleName, string presetChangeType,
        ISecureStorageService? secureStorage = null, bool configurationChanged = true)
    {
        var properties = Module(module, moduleName,
            HasHomeAssistantAccessToken(module, moduleName, secureStorage), secureStorage);
        properties["presetId"] = preset.Id;
        properties["presetChangeType"] = presetChangeType;
        properties["moduleConfigurationChanged"] = configurationChanged;
        return properties;
    }

    public static Dictionary<string, object?> Module(ConfiguredModule module, string moduleName,
        bool? hasAccessToken = null, ISecureStorageService? secureStorage = null)
    {
        var name = NormalizeModuleName(moduleName);
        var settings = module.Settings;
        var properties = new Dictionary<string, object?>
        {
            ["moduleId"] = module.ModuleId,
            ["instanceId"] = module.InstanceId,
            ["moduleName"] = name,
            ["startDelayMs"] = (long)module.StartDelay.TotalMilliseconds
        };

        switch (name)
        {
            case "App Blocker":
                AddBlockerProperties(properties, settings, AppBlockerCategories, "CustomProcessList",
                    "hasCustomProcesses", "customProcessCount", "customProcessNames", NormalizeProcessName);
                break;
            case "Site Blocker":
                AddBlockerProperties(properties, settings, SiteBlockerCategories, "CustomSites",
                    "hasCustomSites", "customSiteCount", "customSiteDomains", NormalizeSiteDomain);
                break;
            case "Application Launcher":
                AddLauncherProperties(properties, settings, secureStorage);
                break;
            case "Home Assistant":
                AddHomeAssistantProperties(properties, settings, hasAccessToken);
                break;
        }

        return properties;
    }

    public static Dictionary<string, object?> ProductState(IEnumerable<SessionPreset> presets,
        IEnumerable<SessionSchedule> schedules)
    {
        var presetList = presets.ToArray();
        var scheduleList = schedules.ToArray();
        var enabledScheduleCount = scheduleList.Count(schedule => schedule.IsEnabled);
        return new Dictionary<string, object?>
        {
            ["presetCount"] = presetList.Length,
            ["scheduleCount"] = scheduleList.Length,
            ["enabledScheduleCount"] = enabledScheduleCount,
            ["hasScheduler"] = scheduleList.Length > 0,
            ["hasCommittedPreset"] = presetList.Any(preset => preset.FocusCommitment.IsCommitted)
        };
    }

    public static Dictionary<string, object?> Schedule(SessionSchedule schedule, string? changeType = null)
    {
        var recurring = schedule.Type is ScheduleType.Recurring or ScheduleType.StopRecurring;
        var canChain = schedule.Type is ScheduleType.StopRecurring or ScheduleType.StopDuration;
        var days = recurring ? DayKeys(schedule.DaysOfWeek) : Array.Empty<string>();
        var properties = new Dictionary<string, object?>
        {
            ["scheduleId"] = schedule.Id,
            ["presetId"] = schedule.PresetId,
            ["enabled"] = schedule.IsEnabled,
            ["scheduleType"] = NormalizeScheduleType(schedule.Type),
            ["daysOfWeek"] = days,
            ["daysCount"] = days.Length,
            ["scheduledMinuteOfDay"] = schedule is
            { Type: ScheduleType.Recurring or ScheduleType.StopRecurring, RecurringTime: { } time }
            && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)
                ? time.Hours * 60 + time.Minutes
                : null,
            ["use24HourFormat"] = schedule.Use24HourFormat,
            ["autoStopDurationMs"] = schedule is { Type: ScheduleType.StopDuration, AutoStopDuration: { } duration }
                ? (long)duration.TotalMilliseconds
                : null,
            ["hasNextPreset"] = canChain && schedule.NextPresetId.HasValue,
            ["nextPresetId"] = canChain ? schedule.NextPresetId : null,
            ["oneTimeMinuteOfDay"] = null,
            ["oneTimeDayOfWeek"] = null,
            ["oneTimeLeadTimeMs"] = null
        };
        if (changeType is not null)
        {
            properties["changeType"] = changeType;
        }

        if (schedule is { Type: ScheduleType.OneTime, OneTimeDate: { } oneTime })
        {
            var local = oneTime.ToLocalTime();
            properties["oneTimeMinuteOfDay"] = local.Hour * 60 + local.Minute;
            properties["oneTimeDayOfWeek"] = DayKey(local.DayOfWeek);
            properties["oneTimeLeadTimeMs"] = (long)Math.Max(0, (oneTime - DateTimeOffset.UtcNow).TotalMilliseconds);
        }

        return properties;
    }

    public static Dictionary<string, object?> ScheduleTriggered(SessionSchedule schedule, string triggerAction,
        string result, Guid? sessionInstanceId = null, string? failureReason = null, string? skipReason = null)
    {
        var properties = Schedule(schedule);
        properties["triggerAction"] = triggerAction;
        properties["result"] = result;
        if (sessionInstanceId.HasValue) properties["sessionInstanceId"] = sessionInstanceId.Value;
        if (failureReason is not null) properties["failureReason"] = failureReason;
        if (skipReason is not null) properties["skipReason"] = skipReason;
        return properties;
    }

    public static string NormalizeActionKey(string key) => key switch
    {
        "AddApp" or "App Blocker.AddApp" => "add_app",
        "RefreshGames" or "Steam.RefreshGames" => "refresh_games",
        "TestConnection" or "Home Assistant.TestConnection" => "test_connection",
        "Login" or "Spotify.Login" => "login",
        "Logout" or "Spotify.Logout" => "logout",
        "InstallExtension.Firefox" or "Site Blocker.InstallExtension.Firefox" => "install_extension_firefox",
        "InstallExtension.Chrome" or "Site Blocker.InstallExtension.Chrome" => "install_extension_chrome",
        "add" or "remove" or "enable" or "disable" or "open" or "close" or "refresh" or "pause" or
            "resume" or "start" or "stop" or "launch" => key,
        _ => "custom"
    };

    public static string FailureReason(Exception exception) => exception switch
    {
        OperationCanceledException => "cancelled",
        TimeoutException => "timeout",
        InvalidSettingsException => "invalid_settings",
        SessionException when exception.Message.Contains("already running", StringComparison.OrdinalIgnoreCase) =>
            "session_already_running",
        SessionException when exception.Message.Contains("protection", StringComparison.OrdinalIgnoreCase) =>
            "protection_unavailable",
        SessionException when exception.Message.Contains("validation", StringComparison.OrdinalIgnoreCase) ||
                              exception.Message.Contains("preflight", StringComparison.OrdinalIgnoreCase) =>
            "validation_failed",
        SessionException when exception.Message.Contains("module", StringComparison.OrdinalIgnoreCase) =>
            "module_unavailable",
        _ => "unknown"
    };

    private static string NormalizeModuleName(string? name) => name?.Trim() switch
    {
        "App Blocker" or "AppBlocker" => "App Blocker",
        "Application Launcher" or "ApplicationLauncher" => "Application Launcher",
        "Home Assistant" or "HomeAssistant" => "Home Assistant",
        "Site Blocker" or "SiteBlocker" => "Site Blocker",
        _ => "custom"
    };

    public static bool HasHomeAssistantAccessToken(ConfiguredModule module, string moduleName,
        ISecureStorageService? secureStorage)
    {
        if (NormalizeModuleName(moduleName) != "Home Assistant") return false;
        if (HasValue(module.Settings, "AccessToken") || HasValue(module.Settings, "HaAccessToken")) return true;
        try
        {
            return !string.IsNullOrWhiteSpace(secureStorage?.RetrieveSecret($"{module.ModuleId}:HaAccessToken"));
        }
        catch
        {
            return false;
        }
    }

    private static void AddBlockerProperties(Dictionary<string, object?> properties,
        IReadOnlyDictionary<string, string> settings, HashSet<string> allowedCategories, string customSetting,
        string hasCustomProperty, string customCountProperty, string customValuesProperty,
        Func<string, string?> normalizeCustomValue)
    {
        properties["blockingMode"] = Value(settings, "Mode") switch
        {
            "BlockList" => "block_list",
            "AllowList" => "allow_list",
            _ => null
        };
        var categories = Values(settings, "Categories").Where(allowedCategories.Contains).Distinct().Order().ToArray();
        properties["categories"] = categories;
        properties["categoryCount"] = categories.Length;
        var customValues = NormalizeEntries(Value(settings, customSetting), normalizeCustomValue);
        properties[hasCustomProperty] = customValues.Length > 0;
        properties[customCountProperty] = customValues.Length;
        properties[customValuesProperty] = customValues;
    }

    private static void AddLauncherProperties(Dictionary<string, object?> properties,
        IReadOnlyDictionary<string, string> settings, ISecureStorageService? secureStorage)
    {
        var applicationPath = Value(settings, "ApplicationPath") ?? string.Empty;
        var launcherModule = ApplicationSelector.GetLauncherModuleKey(applicationPath,
            Value(settings, ApplicationSelector.LegacyModuleKeySetting));
        var customApp = string.Equals(applicationPath, "custom-app", StringComparison.OrdinalIgnoreCase);
        var appType = customApp ? "custom" : launcherModule switch
        {
            "Browser" => "browser",
            "Discord" => "discord",
            "JetBrainsIDE" => "jetbrains",
            "OBS" => "obs",
            "Spotify" => "spotify",
            "Steam" => "steam",
            "VSCode" => "vscode",
            _ => "unknown"
        };
        var useCustomWorkingDirectory = BoolValue(settings, "UseCustomWorkingDirectory");
        var moveToMonitor = BoolValue(settings, "MoveToMonitor");
        var useCustomSize = BoolValue(settings, "UseCustomSize");

        properties["launcherAppType"] = appType;
        properties["processMode"] = Choice(Value(settings, "ProcessMode"),
            ("LaunchNew", "launch_new"), ("AttachExisting", "attach_existing"), ("LaunchOrAttach", "launch_or_attach"));
        properties["windowState"] = Choice(Value(settings, "WindowState"),
            ("Normal", "normal"), ("Maximized", "maximized"), ("Minimized", "minimized"));
        properties["useCustomSize"] = useCustomSize;
        properties["windowWidth"] = useCustomSize == true ? IntValue(settings, "WindowWidth") : null;
        properties["windowHeight"] = useCustomSize == true ? IntValue(settings, "WindowHeight") : null;
        properties["moveToMonitor"] = moveToMonitor;
        properties["targetMonitorConfigured"] = moveToMonitor == true && HasValue(settings, "TargetMonitor");
        properties["targetMonitorIndex"] = moveToMonitor == true &&
            IntValue(settings, "TargetMonitor") is { } monitorIndex && monitorIndex >= 0 ? monitorIndex : null;
        properties["lifecycleMode"] = Choice(Value(settings, "LifecycleMode"),
            ("KeepRunning", "keep_running"), ("TerminateGraceful", "terminate_graceful"),
            ("TerminateForce", "terminate_force"), ("TerminateOnEnd", "terminate_force"));
        properties["bringToForeground"] = BoolValue(settings, "BringToForeground");
        properties["isCustomApp"] = appType == "custom";
        properties["hasApplicationArgs"] = HasValue(settings, "ApplicationArgs");
        properties["hasProjectPath"] = HasValue(settings, "ProjectPath");
        properties["projectPathHash"] = HashPath(Value(settings, "ProjectPath"), secureStorage);
        properties["useCustomWorkingDirectory"] = useCustomWorkingDirectory;
        properties["hasWorkingDirectory"] = useCustomWorkingDirectory == true && HasValue(settings, "WorkingDirectory");
        properties["workingDirectoryHash"] = useCustomWorkingDirectory == true
            ? HashPath(Value(settings, "WorkingDirectory"), secureStorage)
            : null;
        if (customApp)
        {
            properties["customApplicationExecutable"] = ProcessName(Value(settings, "CustomPath"));
            properties["customApplicationPathHash"] = HashPath(Value(settings, "CustomPath"), secureStorage);
        }

        switch (appType)
        {
            case "browser":
                properties["browserFamily"] = BrowserFamily(applicationPath);
                properties["hasStartUrl"] = HasValue(settings, "StartUrl");
                properties["startUrlDomain"] = NormalizeSiteDomain(Value(settings, "StartUrl"));
                properties["hasProfileName"] = HasValue(settings, "ProfileName");
                properties["browserProfileHash"] = HashText(Value(settings, "ProfileName")?.Trim(), secureStorage);
                properties["incognitoMode"] = BoolValue(settings, "IncognitoMode");
                properties["hasAdditionalArgs"] = HasValue(settings, "AdditionalArgs");
                break;
            case "jetbrains":
                properties["ideFamily"] = IdeFamily(applicationPath);
                break;
            case "obs":
                var webSocketEnabled = BoolValue(settings, "EnableWebSocket");
                var webSocketPort = IntValue(settings, "WebSocketPort");
                properties["webSocketEnabled"] = webSocketEnabled;
                properties["webSocketPortIsDefault"] = webSocketEnabled == true
                    ? webSocketPort is null || webSocketPort == 4455
                    : null;
                properties["webSocketPort"] = webSocketEnabled == true && webSocketPort is >= 1 and <= 65535
                    ? webSocketPort
                    : null;
                properties["hasWebSocketPassword"] = webSocketEnabled == true && HasValue(settings, "WebSocketPassword");
                properties["sessionStartAction"] = Choice(Value(settings, "SessionStartAction"),
                    ("none", "none"), ("start_streaming", "start_streaming"),
                    ("start_recording", "start_recording"), ("start_both", "start_both"),
                    ("start_virtualcam", "start_virtualcam"));
                properties["sessionEndAction"] = Choice(Value(settings, "SessionEndAction"),
                    ("none", "none"), ("stop_streaming", "stop_streaming"),
                    ("stop_recording", "stop_recording"), ("stop_both", "stop_both"),
                    ("stop_virtualcam", "stop_virtualcam"), ("stop_all", "stop_all"));
                break;
            case "spotify":
                properties["playbackEnabled"] = BoolValue(settings, "EnablePlayback");
                properties["deviceSelectionMode"] = Choice(Value(settings, "DeviceSelectionMode"),
                    ("LocalComputer", "local_computer"), ("LastActive", "last_active"),
                    ("SpecificName", "specific_name"));
                properties["hasSpecificDeviceName"] =
                    Value(settings, "DeviceSelectionMode") == "SpecificName" && HasValue(settings, "SpecificDeviceName");
                properties["playbackContextKind"] = PlaybackContextKind(Value(settings, "PlaybackContext"));
                properties["hasCustomPlaybackUrl"] =
                    Value(settings, "PlaybackContext") == "custom" && HasValue(settings, "CustomUrl");
                properties["volume"] = IntValue(settings, "Volume");
                properties["shuffle"] = BoolValue(settings, "Shuffle");
                properties["repeatMode"] = Choice(Value(settings, "RepeatMode"),
                    ("off", "off"), ("context", "context"), ("track", "track"));
                break;
            case "steam":
                properties["hasSelectedGame"] = HasValue(settings, "SelectedGame");
                properties["selectedSteamAppId"] = int.TryParse(Value(settings, "SelectedGame"),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var appId) && appId > 0 ? appId : null;
                break;
        }
    }

    private static void AddHomeAssistantProperties(Dictionary<string, object?> properties,
        Dictionary<string, string> settings, bool? hasAccessToken)
    {
        var baseUrl = Value(settings, "BaseUrl");
        properties["hasBaseUrl"] = !string.IsNullOrWhiteSpace(baseUrl);
        properties["usesHttps"] = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
                                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        if (hasAccessToken.HasValue)
        {
            properties["hasAccessToken"] = hasAccessToken.Value;
        }
        else if (settings.ContainsKey("AccessToken") || settings.ContainsKey("HaAccessToken"))
        {
            properties["hasAccessToken"] = HasValue(settings, "AccessToken") || HasValue(settings, "HaAccessToken");
        }
        properties["hasStartEntity"] = HasValue(settings, "StartEntityId");
        properties["hasEndEntity"] = HasValue(settings, "EndEntityId");
        properties["startEntityDomain"] = EntityDomain(Value(settings, "StartEntityId"));
        properties["endEntityDomain"] = EntityDomain(Value(settings, "EndEntityId"));
    }


    private static string BrowserFamily(string path)
    {
        var executable = Path.GetFileName(path).ToLowerInvariant();
        return executable switch
        {
            "chrome.exe" or "msedge.exe" or "brave.exe" or "opera.exe" or "vivaldi.exe" or "chromium.exe" => "chromium",
            "firefox.exe" or "waterfox.exe" or "librewolf.exe" or "floorp.exe" => "firefox",
            "tor.exe" => "tor",
            "arc.exe" => "arc",
            "palemoon.exe" => "palemoon",
            _ => "other"
        };
    }

    private static string IdeFamily(string path)
    {
        var executable = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace("64", string.Empty);
        return executable switch
        {
            "rider" or "clion" or "idea" or "pycharm" or "webstorm" or "goland" or "phpstorm" or
                "rubymine" or "datagrip" => executable,
            _ => "unknown"
        };
    }

    private static string? PlaybackContextKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Equals("custom", StringComparison.OrdinalIgnoreCase)) return "custom";
        var parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var kind = parts.FirstOrDefault(part => part is "playlist" or "album" or "track" or "collection");
        return kind switch
        {
            "playlist" => "playlist",
            "album" => "album",
            "track" => "track",
            "collection" => "liked_songs",
            _ => "other"
        };
    }

    private static string NormalizeScheduleType(ScheduleType type) => type switch
    {
        ScheduleType.OneTime => "one_time",
        ScheduleType.Recurring => "recurring_start",
        ScheduleType.StopRecurring => "recurring_stop",
        ScheduleType.StopDuration => "duration_stop",
        _ => "unknown"
    };


    private static string[] DayKeys(IEnumerable<DayOfWeek>? days) =>
    [
        .. (days ?? [])
        .Where(Enum.IsDefined)
        .Distinct()
        .Order()
        .Select(DayKey)
    ];

    private static string DayKey(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "mon",
        DayOfWeek.Tuesday => "tue",
        DayOfWeek.Wednesday => "wed",
        DayOfWeek.Thursday => "thu",
        DayOfWeek.Friday => "fri",
        DayOfWeek.Saturday => "sat",
        DayOfWeek.Sunday => "sun",
        _ => "unknown"
    };

    private static string? Value(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.GetValueOrDefault(key);

    private static bool HasValue(IReadOnlyDictionary<string, string> settings, string key) =>
        !string.IsNullOrWhiteSpace(Value(settings, key));

    private static int? IntValue(IReadOnlyDictionary<string, string> settings, string key) =>
        int.TryParse(Value(settings, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static bool? BoolValue(IReadOnlyDictionary<string, string> settings, string key) =>
        bool.TryParse(Value(settings, key), out var value) ? value : null;


    private static string[] NormalizeEntries(string? value, Func<string, string?> normalize) =>
        (value ?? string.Empty)
        .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(normalize)
        .Where(item => item is not null)
        .Select(item => item!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string? NormalizeProcessName(string value)
    {
        var name = ProcessName(value);
        return name is not null && ProcessNameRegex().IsMatch(name) ? name : null;
    }

    private static string? ProcessName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = Path.GetFileNameWithoutExtension(value.Trim().Replace('\\', '/'))?.Trim()
            .Replace(' ', '-')
            .ToLowerInvariant();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? NormalizeSiteDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        if (candidate.StartsWith("*.", StringComparison.Ordinal)) candidate = candidate[2..];
        if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = $"https://{candidate}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.HostNameType != UriHostNameType.Dns)
            return null;
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return DomainRegex().IsMatch(host) ? host : null;
    }

    private static string? EntityDomain(string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;
        var separator = entityId.IndexOf('.');
        if (separator < 1) return null;
        var domain = entityId[..separator].Trim().ToLowerInvariant();
        return EntityDomainRegex().IsMatch(domain) ? domain : null;
    }

    private static string? HashPath(string? path, ISecureStorageService? secureStorage)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
            if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
            return HashText(normalized, secureStorage);
        }
        catch
        {
            return null;
        }
    }

    private static string? HashText(string? value, ISecureStorageService? secureStorage)
    {
        if (string.IsNullOrWhiteSpace(value) || secureStorage is null) return null;
        try
        {
            lock (PathHmacLock)
            {
                var encodedKey = secureStorage.RetrieveSecret(PathHmacKey);
                byte[] key;
                if (string.IsNullOrWhiteSpace(encodedKey))
                {
                    key = RandomNumberGenerator.GetBytes(32);
                    secureStorage.StoreSecret(PathHmacKey, Convert.ToBase64String(key));
                }
                else
                {
                    key = Convert.FromBase64String(encodedKey);
                    if (key.Length != 32) return null;
                }

                return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value)));
            }
        }
        catch
        {
            return null;
        }
    }

    private static string[] Values(IReadOnlyDictionary<string, string> settings, string key) =>
        (Value(settings, key) ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Choice(string? value, params (string Input, string Output)[] choices) => (from choice in choices where string.Equals(choice.Input, value, StringComparison.OrdinalIgnoreCase) select choice.Output).FirstOrDefault();

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial System.Text.RegularExpressions.Regex EntityDomainRegex();

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9._+-]{1,100}$")]
    private static partial System.Text.RegularExpressions.Regex ProcessNameRegex();

    [System.Text.RegularExpressions.GeneratedRegex("^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial System.Text.RegularExpressions.Regex DomainRegex();
}
