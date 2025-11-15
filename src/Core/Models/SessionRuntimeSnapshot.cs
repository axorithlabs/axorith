using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;

namespace Axorith.Core.Models;

/// <summary>
///     Immutable snapshot of the currently running session, used as a facade for Host-layer consumers.
/// </summary>
public sealed record SessionSnapshot(
    Guid PresetId,
    string PresetName,
    IReadOnlyList<SessionModuleSnapshot> Modules
);

/// <summary>
///     Immutable snapshot of a single active module instance within a running session.
/// </summary>
public sealed record SessionModuleSnapshot(
    Guid InstanceId,
    Guid ModuleId,
    string ModuleName,
    string? CustomName,
    IReadOnlyList<SessionSettingSnapshot> Settings,
    IReadOnlyList<SessionActionSnapshot> Actions
);

/// <summary>
///     Immutable snapshot of a setting for a module instance.
/// </summary>
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

/// <summary>
///     Immutable snapshot of an action for a module instance.
/// </summary>
public sealed record SessionActionSnapshot(
    string Key,
    string Label,
    bool IsEnabled
);
