namespace Axorith.Sdk;

/// <summary>
///     Represents the severity of a validation result.
/// </summary>
public enum ValidationStatus
{
    /// <summary>
    ///     The settings are valid and the module is ready.
    /// </summary>
    Ok,

    /// <summary>
    ///     The module can probably work, but there's a non-critical issue.
    /// </summary>
    Warning,

    /// <summary>
    ///     The settings are invalid, and the module will fail to start.
    /// </summary>
    Error
}