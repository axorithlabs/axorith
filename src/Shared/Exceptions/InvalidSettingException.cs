namespace Axorith.Shared.Exceptions;

/// <summary>
/// Thrown when a module's settings are invalid and prevent it from functioning correctly.
/// </summary>
public class InvalidSettingsException(string message, IReadOnlyList<string> invalidKeys) : AxorithException(message)
{
    /// <summary>
    /// A list of setting keys that are invalid.
    /// </summary>
    public IReadOnlyList<string> InvalidKeys { get; } = invalidKeys;
}