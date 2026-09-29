using System.Collections;
using System.Text.RegularExpressions;

namespace Axorith.Telemetry;

internal static partial class TelemetryEventSanitizer
{
    private static readonly HashSet<string> GuidProperties = ["installAttemptId", "presetId", "sessionInstanceId", "moduleId", "instanceId", "scheduleId"];
    private static readonly HashSet<string> NumberProperties = [
        "durationMs", "plannedDurationMs", "breakDurationMs", "latencyMs", "moduleCount", "breakCount", "breaksUsed",
        "occurrenceCount", "blockedAttemptCount", "configuredEntryCount", "reconnectCount", "createdCount",
        "releaseVersionNumber"];
    private static readonly HashSet<string> BooleanProperties = [
        "completedAsPlanned", "handled", "fatal", "hasDesktopRuntime", "hasAspNetRuntime",
        "browserExtensionConnected", "protectionDegraded", "emergencyUnlockUsed", "enabled"];
    private static readonly Dictionary<string, HashSet<string>> EnumValues = new(StringComparer.Ordinal)
    {
        ["application"] = ["Axorith.Client", "Axorith.Host", "Axorith.Installer", "Axorith.Website"],
        ["installMode"] = ["fresh", "update", "uninstall"],
        ["launchSource"] = ["manual", "autostart", "installer"],
        ["startSource"] = ["manual", "schedule", "chained", "recovered"],
        ["commitmentMode"] = ["normal", "locked", "strict"],
        ["endConditionType"] = ["none", "duration", "end_at"],
        ["afterEndAction"] = ["do_nothing", "start_next_workspace", "lock_pc", "sleep", "sign_out", "shut_down_pc"],
        ["stopReason"] = ["user_stop", "natural_completion", "emergency_unlock", "startup_failure"],
        ["changeType"] = ["create", "update", "delete"],
        ["result"] = ["success", "failed", "started", "completed", "cancelled", "degraded", "recovered"],
        ["source"] = ["client", "host", "installer"],
        ["stage"] = [
            "prerequisite_download", "prerequisite_install", "existing_version_removal", "files_copy", "registration",
            "uninstall_cleanup",
            "preflight", "session_initialization", "module_validation", "module_startup", "rpc_request",
            "update_check", "update_download", "update_install", "installer_launch", "settings_save",
            "schedule_save", "preset_save", "onboarding"],
        ["failureReason"] = [
            "validation_failed", "protection_unavailable", "module_unavailable", "timeout", "cancelled",
            "session_already_running", "invalid_settings", "network_error", "unknown"],
        ["subsystem"] = [
            "client", "host", "installer", "session", "module", "update", "preset", "schedule",
            "onboarding", "settings", "telemetry", "commitment"],
        ["operation"] = [
            "startup", "session_start", "session_stop", "module_execution", "design_time_action",
            "update_check", "update_download", "update_install", "settings_save", "schedule_save",
            "preset_save", "onboarding_create", "connection_initialize", "unknown"],
        ["severity"] = ["debug", "information", "warning", "error", "fatal"],
        ["protectionState"] = ["inactive", "active", "degraded", "recovered", "failed"],
        ["platform"] = ["windows"],
        ["architecture"] = ["x86", "x64", "arm64"],
        ["build_channel"] = ["stable", "beta", "alpha", "dev"],
        ["environment"] = ["development", "staging", "production"],
        ["ctaLocation"] = ["hero", "bottom_cta", "module_detail", "nav", "mobile_nav"],
        ["moduleName"] = ["App Blocker", "Application Launcher", "Home Assistant", "Site Blocker", "custom"],
        ["actionKey"] = ["add", "remove", "enable", "disable", "open", "close", "login", "logout", "refresh", "pause", "resume", "start", "stop", "launch", "custom"],
        ["category"] = ["startup", "appearance", "telemetry", "tray_behavior"],
    };

    private static readonly HashSet<string> StringProperties = [
        "currentVersion", "previousVersion", "releaseVersion", "axorith_version", "exceptionType", "fingerprint", "diagnostic",
        "os_version", "build_channel", "environment", "application", "installMode", "launchSource", "source",
        "startSource", "commitmentMode", "endConditionType", "afterEndAction", "stopReason", "changeType",
        "result", "stage", "failureReason", "subsystem", "operation", "severity", "protectionState",
        "platform", "architecture", "ctaLocation", "moduleName", "actionKey", "category"];

    public static IReadOnlyDictionary<string, object?> SanitizeProperties(
        IReadOnlyDictionary<string, object?> properties)
    {
        var sanitized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            var safe = SanitizeValue(key, value);
            if (safe is not null)
            {
                sanitized[key] = safe;
            }
        }

        return sanitized;
    }

    private static object? SanitizeValue(string key, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (key == TelemetryConstants.Properties.Set && value is IReadOnlyDictionary<string, object?> set)
        {
            return SanitizeProperties(set);
        }

        if (GuidProperties.Contains(key))
        {
            if (value is Guid id) return id.ToString();
            return Guid.TryParse(value.ToString(), out id) ? id.ToString() : null;
        }

        if (NumberProperties.Contains(key))
        {
            return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                ? value
                : null;
        }

        if (BooleanProperties.Contains(key))
        {
            return value is bool flag ? flag : null;
        }

        if (key == "modules")
        {
            return SanitizeModules(value);
        }

        if (key is "moduleIds" or "moduleTypes" or "createdModuleTypes" or "changedCategories")
        {
            return SanitizeStringList(key, value);
        }

        if (!StringProperties.Contains(key) || value is not string text)
        {
            return null;
        }

        if (EnumValues.TryGetValue(key, out var allowed))
        {
            if (allowed.Contains(text)) return text;
            if (key == "moduleName" && text != "custom") return "custom";
            if (key == "actionKey") return "custom";
            return null;
        }

        return key switch
        {
            "currentVersion" or "previousVersion" or "releaseVersion" or "axorith_version" => SafeVersion(text),
            "exceptionType" => SafeIdentifier(text, 128),
            "fingerprint" => FingerprintRegex().IsMatch(text) ? text : null,
            "diagnostic" => SafeIdentifier(text, 160),
            "os_version" => SafeOsVersion(text),
            _ => null
        };
    }

    private static object? SanitizeModules(object value)
    {
        if (value is not IEnumerable items || value is string)
        {
            return null;
        }

        var safeModules = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var item in items.Cast<object?>().Take(64))
        {
            if (item is not IDictionary<string, object?> dictionary) continue;
            var safe = SanitizeProperties(new Dictionary<string, object?>(dictionary));
            if (safe.Count > 0) safeModules.Add(safe);
        }

        return safeModules;
    }

    private static object? SanitizeStringList(string key, object value)
    {
        if (value is not IEnumerable items || value is string) return null;

        var result = new List<string>();
        foreach (var item in items.Cast<object?>().Take(64))
        {
            if (item is not string text) continue;
            string? safe = key switch
            {
                "moduleIds" => Guid.TryParse(text, out var id) ? id.ToString() : null,
                "moduleTypes" => EnumValues["moduleName"].Contains(text) ? text : "custom",
                "createdModuleTypes" => text is "Developer" or "Gamer" or "Streamer" ? text : null,
                "changedCategories" => EnumValues["category"].Contains(text) ? text : null,
                _ => null
            };
            if (safe is not null) result.Add(safe);
        }

        return result;
    }

    private static string? SafeVersion(string value) => VersionRegex().IsMatch(value) ? value : null;
    private static string? SafeOsVersion(string value) => OsVersionRegex().IsMatch(value) ? value : null;
    private static string? SafeIdentifier(string value, int maxLength) =>
        value.Length <= maxLength && IdentifierRegex().IsMatch(value) ? value : null;

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$")]
    private static partial Regex VersionRegex();

    [GeneratedRegex("^[A-Za-z0-9 ._-]{1,80}$")]
    private static partial Regex OsVersionRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.+`]{0,159}$")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex FingerprintRegex();
}
