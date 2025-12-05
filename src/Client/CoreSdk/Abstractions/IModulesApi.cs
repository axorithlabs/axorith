using Axorith.Sdk;

namespace Axorith.Client.CoreSdk.Abstractions;

/// <summary>
///     API for module discovery and interaction.
/// </summary>
public interface IModulesApi
{
    /// <summary>
    ///     Lists all available module definitions loaded in the host.
    /// </summary>
    Task<IReadOnlyList<ModuleDefinition>> ListModulesAsync(CancellationToken ct = default);

    /// <summary>
    ///     Gets the settings and actions for a module definition.
    ///     Used by the client to display module configuration UI.
    /// </summary>
    Task<ModuleSettingsInfo> GetModuleSettingsAsync(Guid moduleId, CancellationToken ct = default);

    /// <summary>
    ///     Invokes an action on a running module instance (runtime).
    ///     Only works when a session is active.
    /// </summary>
    Task<OperationResult> InvokeActionAsync(Guid moduleInstanceId, string actionKey,
        CancellationToken ct = default);

    /// <summary>
    ///     Invokes a design-time-only action on a module definition using a temporary module instance.
    ///     Used for actions like OAuth login while editing presets.
    /// </summary>
    Task<OperationResult> InvokeDesignTimeActionAsync(Guid moduleId, Guid moduleInstanceId, string actionKey,
        CancellationToken ct = default);

    /// <summary>
    ///     Updates a setting value on a running module instance.
    ///     Only works when a session is active.
    /// </summary>
    Task<OperationResult> UpdateSettingAsync(Guid moduleInstanceId, string settingKey, object? value,
        CancellationToken ct = default);

    /// <summary>
    ///     Starts a design-time edit session for the specified module instance.
    ///     Initializes a sandbox on the Host with the provided initial values snapshot.
    /// </summary>
    Task<BeginEditResult> BeginEditAsync(Guid moduleId, Guid moduleInstanceId,
        IReadOnlyDictionary<string, object?> initialValues, CancellationToken ct = default);

    /// <summary>
    ///     Ends a design-time edit session and disposes the sandbox on the Host.
    /// </summary>
    Task<OperationResult> EndEditAsync(Guid moduleInstanceId, CancellationToken ct = default);

    /// <summary>
    ///     Requests the Host to re-broadcast current sandbox reactive state for this module instance.
    ///     Useful right after loading the settings to apply visibility/labels/read-only.
    /// </summary>
    Task<OperationResult> SyncEditAsync(Guid moduleInstanceId, CancellationToken ct = default);

    /// <summary>
    ///     Validates a set of settings against the module's validation logic.
    ///     Uses the design-time sandbox on the Host.
    /// </summary>
    Task<ValidationResult> ValidateSettingsAsync(Guid moduleId, Guid moduleInstanceId,
        IReadOnlyDictionary<string, object?> values, CancellationToken ct = default);

    /// <summary>
    ///     Observable stream of setting updates from running modules.
    ///     Broadcasts reactive changes (label, visibility, choices, etc.).
    /// </summary>
    IObservable<SettingUpdate> SettingUpdates { get; }

    /// <summary>
    ///     Starts a filtered gRPC stream for setting updates of a specific module instance.
    ///     Returns a handle that must be disposed to stop the stream.
    ///     Waits for the stream connection to be established before completing.
    /// </summary>
    Task<IDisposable> SubscribeToSettingUpdatesAsync(Guid moduleInstanceId);

    /// <summary>
    ///     Returns the last cached ModuleSettingsInfo for the module if available.
    /// </summary>
    ModuleSettingsInfo? GetCachedSettings(Guid moduleId);
}

/// <summary>
///     Notification of a setting property update from a running module.
/// </summary>
public record SettingUpdate(
    Guid ModuleInstanceId,
    string SettingKey,
    SettingProperty Property,
    object? Value
);

/// <summary>
///     Properties that can be updated reactively on a setting.
/// </summary>
public enum SettingProperty
{
    /// <summary>The setting's value changed.</summary>
    Value,

    /// <summary>The setting's label changed.</summary>
    Label,

    /// <summary>The setting's visibility changed.</summary>
    Visibility,

    /// <summary>The setting's read-only state changed.</summary>
    ReadOnly,

    /// <summary>The setting's available choices changed.</summary>
    Choices,

    /// <summary>An action's enabled state changed.</summary>
    ActionEnabled,

    /// <summary>An action's label changed.</summary>
    ActionLabel
}

/// <summary>
///     Module settings and actions information for UI display.
/// </summary>
public record ModuleSettingsInfo(
    IReadOnlyList<ModuleSetting> Settings,
    IReadOnlyList<ModuleAction> Actions
);

/// <summary>
///     Setting information for UI display.
/// </summary>
public record ModuleSetting(
    string Key,
    string Label,
    string? Description,
    string ControlType,
    string Persistence,
    bool IsVisible,
    bool IsReadOnly,
    string ValueType,
    string CurrentValue,
    IReadOnlyList<KeyValuePair<string, string>> Choices,
    string? Filter,
    bool HasHistory
);

/// <summary>
///     Action information for UI display.
/// </summary>
public record ModuleAction(
    string Key,
    string Label,
    string? Description,
    bool IsEnabled
);

/// <summary>
///     Result of BeginEdit: schema plus status.
/// </summary>
public record BeginEditResult(
    ModuleSettingsInfo SettingsInfo,
    OperationResult Result
);