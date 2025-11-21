using Avalonia.Data.Converters;

namespace Axorith.Client.Converters;

/// <summary>
///     Provides static utility converters for the application.
/// </summary>
public static class AppConverters
{
    public static readonly IValueConverter IsNotNullOrEmpty =
        new FuncValueConverter<string?, bool>(value => !string.IsNullOrEmpty(value));

    public static readonly IValueConverter IsNull =
        new FuncValueConverter<object?, bool>(value => value is null);

    /// <summary>
    ///     A multi-value converter that returns true if all values in the binding are equal.
    /// </summary>
    public static readonly IMultiValueConverter MultiEqualsConverter =
        new FuncMultiValueConverter<IEnumerable<object?>, bool>(bindings =>
        {
            var values = bindings.ToList();
            if (values.Count < 2)
            {
                return true;
            }

            var first = values[0];
            for (var i = 1; i < values.Count; i++)
                if (!Equals(first, values[i]))
                {
                    return false;
                }

            return true;
        });

    public static readonly IValueConverter BoolToDoubleConverter =
        new FuncValueConverter<bool, double>(v => v ? 1.0 : 0.0);
}