using Axorith.Sdk.Settings;

namespace Axorith.Sdk;

/// <summary>
///     Represents the metadata required to render and edit a module setting from the Client UI.
/// </summary>
public record ModuleSettingDefinition(
    string Key,
    string Label,
    string? Description,
    SettingControlType ControlType,
    SettingPersistence Persistence,
    bool IsVisible,
    bool IsReadOnly,
    string ValueTypeName,
    string RawValue,
    IReadOnlyList<KeyValuePair<string, string>> Choices);