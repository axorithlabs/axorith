namespace Axorith.Shared.Exceptions;

public class InvalidSettingsException(string message, IReadOnlyList<string> invalidKeys) : Exception(message)
{
    public IReadOnlyList<string> InvalidKeys { get; } = invalidKeys;
}
