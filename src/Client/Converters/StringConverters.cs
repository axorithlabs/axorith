using Avalonia.Data.Converters;

namespace Axorith.Client.Converters;

/// <summary>
/// Provides static utility converters for string checks.
/// </summary>
public static class StringConverters
{
    /// <summary>
    /// Checks if a string value is NOT null or empty.
    /// </summary>
    public static readonly IValueConverter IsNotNullOrEmpty = new FuncValueConverter<string?, bool>(
        (value) => !string.IsNullOrEmpty(value)
    );
}