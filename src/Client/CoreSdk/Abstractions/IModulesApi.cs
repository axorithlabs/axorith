using Axorith.Sdk;

namespace Axorith.Client.CoreSdk.Abstractions;

public interface IModulesApi
{
    Task<IReadOnlyList<ModuleDefinition>> ListModulesAsync(CancellationToken ct = default);

    Task<ModuleSettingsInfo> GetModuleSettingsAsync(Guid moduleId, CancellationToken ct = default);

    Task<OperationResult> InvokeActionAsync(Guid moduleInstanceId, string actionKey,
        CancellationToken ct = default);

    Task<OperationResult> InvokeDesignTimeActionAsync(Guid moduleId, Guid moduleInstanceId, string actionKey,
        CancellationToken ct = default);

    Task<OperationResult> UpdateSettingAsync(Guid moduleInstanceId, string settingKey, object? value,
        CancellationToken ct = default);

    Task<BeginEditResult> BeginEditAsync(Guid moduleId, Guid moduleInstanceId,
        IReadOnlyDictionary<string, object?> initialValues, CancellationToken ct = default);

    Task<OperationResult> EndEditAsync(Guid moduleInstanceId, CancellationToken ct = default);

    Task<OperationResult> SyncEditAsync(Guid moduleInstanceId, CancellationToken ct = default);

    Task<ValidationResult> ValidateSettingsAsync(Guid moduleId, Guid moduleInstanceId,
        IReadOnlyDictionary<string, object?> values, CancellationToken ct = default);

    IObservable<SettingUpdate> SettingUpdates { get; }

    Task<IDisposable> SubscribeToSettingUpdatesAsync(Guid moduleInstanceId);

    ModuleSettingsInfo? GetCachedSettings(Guid moduleId);
}

public record SettingUpdate(
    Guid ModuleInstanceId,
    string SettingKey,
    SettingProperty Property,
    object? Value
);

public enum SettingProperty
{
    Value,

    Label,

    Visibility,

    ReadOnly,

    Choices,

    ActionEnabled,

    ActionLabel
}

public record ModuleSettingsInfo(
    IReadOnlyList<ModuleSetting> Settings,
    IReadOnlyList<ModuleAction> Actions
);

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

public record ModuleAction(
    string Key,
    string Label,
    string? Description,
    bool IsEnabled,
    string? SettingKey = null
);

public record BeginEditResult(
    ModuleSettingsInfo SettingsInfo,
    OperationResult Result
);
