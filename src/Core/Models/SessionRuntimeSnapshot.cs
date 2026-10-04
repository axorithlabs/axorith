using Axorith.Sdk.Settings;

namespace Axorith.Core.Models;

public sealed record SessionSnapshot(
    Guid PresetId,
    string PresetName,
    IReadOnlyList<SessionModuleSnapshot> Modules
);

public sealed record SessionModuleSnapshot(
    Guid InstanceId,
    Guid ModuleId,
    string ModuleName,
    string? CustomName,
    IReadOnlyList<SessionSettingSnapshot> Settings,
    IReadOnlyList<SessionActionSnapshot> Actions
);

public sealed record SessionSettingSnapshot(
    string Key,
    string Label,
    string? Description,
    SettingControlType ControlType,
    SettingPersistence Persistence,
    bool IsReadOnly,
    bool IsVisible,
    string ValueType,
    string ValueString
);

public sealed record SessionActionSnapshot(
    string Key,
    string Label,
    bool IsEnabled
);
