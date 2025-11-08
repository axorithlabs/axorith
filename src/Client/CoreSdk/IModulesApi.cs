using Axorith.Sdk;

namespace Axorith.Client.CoreSdk;

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
    ///     Invokes an action on a running module instance.
    ///     Only works when a session is active.
    /// </summary>
    Task<OperationResult> InvokeActionAsync(Guid moduleInstanceId, string actionKey,
        CancellationToken ct = default);

    /// <summary>
    ///     Updates a setting value on a running module instance.
    ///     Only works when a session is active.
    /// </summary>
    Task<OperationResult> UpdateSettingAsync(Guid moduleInstanceId, string settingKey, object? value,
        CancellationToken ct = default);

    /// <summary>
    ///     Observable stream of setting updates from running modules.
    ///     Broadcasts reactive changes (label, visibility, choices, etc.).
    /// </summary>
    IObservable<SettingUpdate> SettingUpdates { get; }
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
    Choices
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
    IReadOnlyList<KeyValuePair<string, string>> Choices
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