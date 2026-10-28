using System.Collections;
using System.Text.RegularExpressions;

namespace Axorith.Telemetry;

internal static partial class TelemetryEventSanitizer
{
    private static readonly HashSet<string> GuidProperties = [
        "oldId", "installAttemptId", "uninstallAttemptId", "presetId", "sessionInstanceId", "moduleId", "instanceId", "scheduleId",
        "nextWorkspaceId", "nextPresetId", "previousSessionInstanceId"];
    private static readonly HashSet<string> NumberProperties = [
        "durationMs", "plannedDurationMs", "breakDurationMs", "latencyMs", "moduleCount", "breakCount", "breaksUsed",
        "occurrenceCount", "blockedAttemptCount", "configuredEntryCount", "reconnectCount", "createdCount",
        "errorCode", "nativeErrorCode", "httpStatusCode",
        "presetCount", "scheduleCount", "enabledScheduleCount", "presetVersion",
        "endAtMinuteOfDay", "scheduleLockMinutes", "startDelayMs", "targetMonitorIndex", "webSocketPort",
        "selectedSteamAppId",
        "windowWidth", "windowHeight", "categoryCount", "customProcessCount", "customSiteCount", "volume",
        "daysCount",
        "scheduledMinuteOfDay", "oneTimeMinuteOfDay", "oneTimeLeadTimeMs", "autoStopDurationMs"];
    private static readonly HashSet<string> BooleanProperties = [
        "completedAsPlanned", "handled", "fatal", "hasDesktopRuntime", "hasAspNetRuntime",
        "browserExtensionConnected", "protectionDegraded", "emergencyUnlockUsed", "enabled", "endAtConfigured",
        "hasNextWorkspace", "hasNextPreset", "useCustomSize", "moveToMonitor", "targetMonitorConfigured", "bringToForeground",
        "isCustomApp", "hasApplicationArgs", "hasProjectPath", "useCustomWorkingDirectory", "hasWorkingDirectory",
        "hasStartUrl", "hasProfileName", "incognitoMode", "hasAdditionalArgs", "webSocketEnabled",
        "webSocketPortIsDefault", "playbackEnabled", "hasSpecificDeviceName", "hasCustomPlaybackUrl", "shuffle",
        "hasSelectedGame", "hasBaseUrl", "usesHttps", "hasAccessToken", "hasStartEntity", "hasEndEntity",
        "hasCustomProcesses", "hasCustomSites", "use24HourFormat", "moduleConfigurationChanged", "hasScheduler",
        "hasCommittedPreset", "hasWebSocketPassword", "dev_mode"];
    private static readonly Dictionary<string, HashSet<string>> EnumValues = new(StringComparer.Ordinal)
    {
        ["name"] = ["Axorith.Client", "Axorith.Host", "Axorith.Installer"],
        ["installMode"] = ["fresh", "update", "uninstall"],
        ["launchSource"] = ["manual", "autostart", "installer"],
        ["startSource"] = ["manual", "schedule", "chained", "recovered"],
        ["commitmentMode"] = ["normal", "locked", "strict"],
        ["endConditionType"] = ["none", "duration", "end_at"],
        ["afterEndAction"] = ["do_nothing", "start_next_workspace", "lock_pc", "sleep", "sign_out", "shut_down_pc"],
        ["stopReason"] = ["user_stop", "natural_completion", "emergency_unlock", "startup_failure"],
        ["changeType"] = ["create", "update", "delete"],
        ["presetChangeType"] = ["create", "update"],
        ["result"] = ["success", "failed", "started", "completed", "cancelled", "degraded", "recovered", "skipped", "warning"],
        ["source"] = ["client", "host", "installer"],
        ["stage"] = [
            "prerequisite_download", "prerequisite_install", "existing_version_removal", "files_copy", "registration",
            "uninstall_cleanup",
            "preflight", "session_initialization", "module_validation", "module_startup", "rpc_request",
            "update_check", "update_download", "update_install", "installer_launch", "settings_save",
            "schedule_save", "preset_save", "onboarding"],
        ["failureReason"] = [
            "validation_failed", "protection_unavailable", "module_unavailable", "timeout", "cancelled",
            "session_already_running", "invalid_settings", "network_error", "preset_not_found", "validation_warning",
            "unknown"],
        ["validationReason"] = ["no_sites_configured", "extension_not_connected", "extension_disabled",
            "extension_inactive", "native_messaging_unavailable"],
        ["skipReason"] = ["session_already_running", "session_not_running", "committed_session",
            "session_ended_before_stop", "session_changed", "concurrent_stop", "preset_mismatch"],
        ["scheduleType"] = ["one_time", "recurring_start", "recurring_stop", "duration_stop", "unknown"],
        ["triggerAction"] = ["start", "stop"],
        ["blockingMode"] = ["block_list", "allow_list"],
        ["launcherAppType"] = ["browser", "discord", "jetbrains", "obs", "spotify", "steam", "vscode", "custom", "unknown"],
        ["processMode"] = ["launch_new", "attach_existing", "launch_or_attach"],
        ["windowState"] = ["normal", "maximized", "minimized"],
        ["lifecycleMode"] = ["keep_running", "terminate_graceful", "terminate_force"],
        ["browserFamily"] = ["chromium", "firefox", "tor", "arc", "palemoon", "other"],
        ["ideFamily"] = ["rider", "clion", "idea", "pycharm", "webstorm", "goland", "phpstorm", "rubymine", "datagrip", "unknown"],
        ["sessionStartAction"] = ["none", "start_streaming", "start_recording", "start_both", "start_virtualcam"],
        ["sessionEndAction"] = ["none", "stop_streaming", "stop_recording", "stop_both", "stop_virtualcam", "stop_all"],
        ["deviceSelectionMode"] = ["local_computer", "last_active", "specific_name"],
        ["playbackContextKind"] = ["custom", "playlist", "album", "track", "liked_songs", "other"],
        ["repeatMode"] = ["off", "context", "track"],
        ["oneTimeDayOfWeek"] = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"],
        ["subsystem"] = [
            "client", "host", "installer", "session", "module", "update", "preset", "schedule",
            "onboarding", "settings", "telemetry", "commitment"],
        ["operation"] = [
            "startup", "session_start", "session_stop", "module_execution", "design_time_action",
            "update_check", "update_download", "update_install", "settings_save", "schedule_save",
            "preset_save", "onboarding_create", "connection_initialize", "rpc_request", "unknown"],
        ["severity"] = ["debug", "information", "warning", "error", "fatal"],
        ["protectionState"] = ["inactive", "active", "degraded", "recovered", "failed"],
        ["platform"] = ["windows"],
        ["architecture"] = ["x86", "x64", "arm64"],
        ["build_channel"] = ["stable", "beta", "alpha", "dev"],
        ["environment"] = ["development", "staging", "production"],
        ["type"] = ["App Blocker", "Application Launcher", "Home Assistant", "Site Blocker", "custom"],
        ["actionKey"] = ["add", "remove", "enable", "disable", "open", "close", "login", "logout", "refresh", "pause", "resume", "start", "stop", "launch", "add_app", "refresh_games", "test_connection", "install_extension_firefox", "install_extension_chrome", "custom"],
        ["category"] = ["startup", "appearance", "telemetry", "tray_behavior"],
    };

    private static readonly HashSet<string> StringProperties = [
        "version", "previousVersion", "targetVersion", "build_sha", "exceptionType", "fingerprint", "diagnostic",
        "errorMessage", "innerExceptionType", "innerErrorMessage", "rpcMethod",
        "os_version", "build_channel", "environment", "name", "installMode", "launchSource", "source",
        "startSource", "commitmentMode", "endConditionType", "afterEndAction", "stopReason", "changeType",
        "result", "stage", "failureReason", "validationReason", "subsystem", "operation", "severity", "protectionState",
        "platform", "architecture", "type", "actionKey", "category", "presetChangeType",
        "skipReason", "scheduleType", "triggerAction", "blockingMode", "launcherAppType", "processMode",
        "windowState", "lifecycleMode", "browserFamily", "ideFamily", "sessionStartAction", "sessionEndAction",
        "deviceSelectionMode", "playbackContextKind", "repeatMode", "oneTimeDayOfWeek", "customApplicationExecutable",
        "startUrlDomain", "startEntityDomain", "endEntityDomain"];
    private static readonly HashSet<string> HashProperties = [
        "projectPathHash", "workingDirectoryHash", "customApplicationPathHash", "browserProfileHash"];

    private static readonly Dictionary<string, HashSet<string>> ObjectProperties = new(StringComparer.Ordinal)
    {
        ["application"] = ["name", "version", "build_sha", "os_version", "build_channel", "environment", "dev_mode",
            "source", "platform", "architecture", "hasDesktopRuntime", "hasAspNetRuntime"],
        ["preset"] = ["presetId", "presetVersion", "commitmentMode", "endConditionType", "plannedDurationMs",
            "endAtConfigured", "endAtMinuteOfDay", "endAtDaysOfWeek", "breakCount", "breakDurationMs",
            "afterEndAction", "hasNextWorkspace", "nextWorkspaceId", "scheduleLockMinutes", "moduleCount", "modules"],
        ["schedule"] = ["scheduleId", "presetId", "enabled", "scheduleType", "daysOfWeek", "daysCount",
            "scheduledMinuteOfDay", "use24HourFormat", "autoStopDurationMs", "hasNextPreset", "nextPresetId",
            "oneTimeMinuteOfDay", "oneTimeDayOfWeek"],
        ["module"] = ["moduleId", "instanceId", "type"]
    };

    public static IReadOnlyDictionary<string, object?> SanitizeProperties(
        IReadOnlyDictionary<string, object?> properties) => SanitizeProperties(properties, null);

    private static IReadOnlyDictionary<string, object?> SanitizeProperties(
        IReadOnlyDictionary<string, object?> properties, string? container)
    {
        var sanitized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            // Exceptions are input only; never serialize their Data, stack traces, or arbitrary members.
            if (container is null && key == "exception" && value is Exception exception)
            {
                sanitized["exceptionType"] = exception.GetType().Name;
                sanitized["errorMessage"] = SanitizeErrorMessage(exception.Message);
                sanitized["errorCode"] = exception.HResult;
                if (exception is System.ComponentModel.Win32Exception native)
                    sanitized["nativeErrorCode"] = native.NativeErrorCode;
                if (exception is HttpRequestException { StatusCode: { } status })
                    sanitized["httpStatusCode"] = (int)status;
                if (exception.GetBaseException() is { } cause && cause != exception)
                {
                    sanitized["innerExceptionType"] = cause.GetType().Name;
                    sanitized["innerErrorMessage"] = SanitizeErrorMessage(cause.Message);
                }
                if (exception is AggregateException aggregate)
                    sanitized["errors"] = SanitizeStringList("errors",
                        aggregate.Flatten().InnerExceptions.Select(error => error.Message));
                continue;
            }
            if (container is not null && ObjectProperties.TryGetValue(container, out var allowed) &&
                !allowed.Contains(key)) continue;
            var safe = SanitizeValue(key, value, container);
            if (safe is not null) sanitized[key] = safe;
        }

        return sanitized;
    }

    private static object? SanitizeValue(string key, object? value, string? container)
    {
        if (value is null) return null;

        if (key is "errorMessage" or "innerErrorMessage")
            return container is null && value is string message ? SanitizeErrorMessage(message) : null;
        if (key == "errors") return container is null ? SanitizeStringList(key, value) : null;

        if (container is null && key == TelemetryConstants.Properties.Set &&
            value is IReadOnlyDictionary<string, object?> set)
            return SanitizeProperties(set);

        if (container is null && ObjectProperties.ContainsKey(key) &&
            value is IReadOnlyDictionary<string, object?> nested)
        {
            var safe = SanitizeProperties(nested, key);
            return safe.Count > 0 ? safe : null;
        }

        if (key is "name" or "version" && container != "application") return null;

        if (GuidProperties.Contains(key))
        {
            if (value is Guid id) return id.ToString();
            return Guid.TryParse(value.ToString(), out id) ? id.ToString() : null;
        }

        if (key == "newId")
            return value is string identity && identity.Length is > 0 and <= 256 && identity.Length % 2 == 0 &&
                   identity.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? identity : null;

        if (NumberProperties.Contains(key))
        {
            if (value is double d && !double.IsFinite(d) || value is float f && !float.IsFinite(f)) return null;
            return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                ? value
                : null;
        }

        if (BooleanProperties.Contains(key)) return value is bool flag ? flag : null;

        if (HashProperties.Contains(key))
            return value is string hash && FingerprintRegex().IsMatch(hash) ? hash : null;

        if (key == "modules") return container == "preset" ? SanitizeModules(value) : null;

        if (key is "createdModuleTypes" or "changedCategories" or
            "categories" or "daysOfWeek" or "endAtDaysOfWeek" or "customProcessNames" or "customSiteDomains")
        {
            return SanitizeStringList(key, value);
        }

        if (!StringProperties.Contains(key) || value is not string text) return null;

        if (EnumValues.TryGetValue(key, out var allowed))
        {
            if (allowed.Contains(text)) return text;
            if (key == "type" && text != "custom") return "custom";
            if (key == "actionKey") return "custom";
            return null;
        }

        return key switch
        {
            "version" or "previousVersion" or "targetVersion" => VersionRegex().IsMatch(text) ? text : null,
            "build_sha" => BuildShaRegex().IsMatch(text) ? text : null,
            "exceptionType" or "innerExceptionType" => SafeIdentifier(text, 128),
            "fingerprint" => FingerprintRegex().IsMatch(text) ? text : null,
            "diagnostic" => SafeIdentifier(text, 160),
            "rpcMethod" => RpcMethodRegex().IsMatch(text) ? text : null,
            "os_version" => OsVersionRegex().IsMatch(text) ? text : null,
            "customApplicationExecutable" => ProcessNameRegex().IsMatch(text) ? text : null,
            "startUrlDomain" => DomainRegex().IsMatch(text) ? text : null,
            "startEntityDomain" or "endEntityDomain" => EntityDomainRegex().IsMatch(text) ? text : null,
            _ => null
        };
    }

    private static object? SanitizeModules(object value)
    {
        if (value is not IEnumerable items || value is string) return null;

        List<IReadOnlyDictionary<string, object?>> safeModules = [];
        foreach (var item in items.Cast<object?>().Take(64))
        {
            if (item is not IDictionary<string, object?> dictionary) continue;
            var safe = SanitizeProperties(new Dictionary<string, object?>(dictionary), "moduleConfiguration");
            if (safe.Count > 0) safeModules.Add(safe);
        }

        return safeModules;
    }

    private static object? SanitizeStringList(string key, object value)
    {
        if (value is not IEnumerable items || value is string) return null;

        List<string> result = [];
        foreach (var item in items.Cast<object?>().Take(key == "errors" ? 8 : 64))
        {
            if (item is not string text) continue;
            string? safe = key switch
            {
                "createdModuleTypes" => text is "Developer" or "Gamer" or "Streamer" ? text : null,
                "changedCategories" => EnumValues["category"].Contains(text) ? text : null,
                "categories" => CategoryValues.Contains(text) ? text : null,
                "daysOfWeek" or "endAtDaysOfWeek" => DayValues.Contains(text) ? text : null,
                "customProcessNames" => ProcessNameRegex().IsMatch(text) ? text : null,
                "customSiteDomains" => DomainRegex().IsMatch(text) ? text : null,
                "errors" => SanitizeErrorMessage(text),
                _ => null
            };
            if (safe is not null) result.Add(safe);
        }

        return result;
    }

    private static string SanitizeErrorMessage(string message)
    {
        if (message.Length > 4096) return "Error message exceeded the telemetry size limit.";
        try
        {
            var text = ResourceNameRegex().Replace(message, "$1[redacted]");
            text = QuotedValueRegex().Replace(text, "[redacted]");
            text = SecretValueRegex().Replace(text, "[redacted]");
            text = PrivateLocationRegex().Replace(text, "[redacted]");
            text = OpaqueValueRegex().Replace(text, "[redacted]");
            text = WhitespaceRegex().Replace(text, " ").Trim();
            return text[..Math.Min(text.Length, 1024)];
        }
        catch (RegexMatchTimeoutException)
        {
            return "[redacted]";
        }
    }

    private static string? SafeIdentifier(string value, int maxLength) =>
        value.Length <= maxLength && IdentifierRegex().IsMatch(value) ? value : null;

    private static readonly HashSet<string> CategoryValues = [
        "Gaming", "Social", "Browsers", "Entertainment", "Productivity", "Email", "Development", "Design", "Office",
        "Video", "Streaming", "News", "Shopping", "Music", "Work", "Adult", "Gambling", "Dating", "Forums"];
    private static readonly HashSet<string> DayValues = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    [GeneratedRegex("^[0-9a-fA-F]{7,64}$")]
    private static partial Regex BuildShaRegex();

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$")]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^[A-Za-z0-9 ._-]{1,80}$")]
    private static partial Regex OsVersionRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.+`]{0,159}$")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex FingerprintRegex();

    [GeneratedRegex("^[a-z0-9._+-]{1,100}$")]
    private static partial Regex ProcessNameRegex();

    [GeneratedRegex("^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex DomainRegex();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex EntityDomainRegex();

    [GeneratedRegex(@"(?s)(\b(?:Module|preset|create) )'.*'(?= (?:validation failed:|validation timed out\.|startup timed out\.|failed to start[.:])|: |\. Aborting\.)", RegexOptions.None, 100)]
    private static partial Regex ResourceNameRegex();

    [GeneratedRegex("(?s)\"[^\"]*\"|(?<![A-Za-z])'[^']*'(?![A-Za-z])", RegexOptions.None, 100)]
    private static partial Regex QuotedValueRegex();

    [GeneratedRegex(@"(?i)\b(?:bearer\s+\S+|(?:password|passwd|token|secret|api[_-]?key|authorization|cookie|credential|access[_-]?token|refresh[_-]?token)\s*[:=]\s*[^\s,;]+)", RegexOptions.None, 100)]
    private static partial Regex SecretValueRegex();

    [GeneratedRegex("(?i)\\b[a-z][a-z0-9+.-]*://[^\\s<>\"']+|(?<!\\w)(?:[a-z]:[\\\\/]|\\\\\\\\)[^\\r\\n;]*|(?<!\\w)/(?:[^/\\s]+/)+[^\\s,;]*|[a-z0-9_.+-]+@[a-z0-9.-]+\\.[a-z]{2,}|\\b(?:[a-z0-9-]+\\.)+[a-z]{2,}\\b|\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b|(?<!\\w)(?:[0-9a-f]{0,4}:){2,}[0-9a-f:.%]+", RegexOptions.None, 100)]
    private static partial Regex PrivateLocationRegex();

    [GeneratedRegex(@"[A-Za-z0-9_+/=-]{24,}", RegexOptions.None, 100)]
    private static partial Regex OpaqueValueRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^/[A-Za-z0-9_.]+/[A-Za-z0-9_]+$")]
    private static partial Regex RpcMethodRegex();

}
