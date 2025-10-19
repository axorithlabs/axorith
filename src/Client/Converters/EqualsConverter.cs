using Avalonia.Data.Converters;
using System.Globalization;

namespace Axorith.Client.Converters;

/// <summary>
/// A value converter that checks for equality between the binding value and a parameter.
/// Typically used to control visibility based on an enum or type.
/// </summary>
public class EqualsConverter : IValueConverter
{
    /// <summary>
    /// A static instance of the converter to be used in XAML.
    /// </summary>
    public static readonly EqualsConverter Instance = new();

    /// <summary>
    /// Compares the input value with the converter parameter.
    /// </summary>
    /// <returns>True if the value and parameter are equal, otherwise false.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.Equals(parameter) ?? parameter == null;
    }

    /// <summary>
    /// This method is not implemented and will throw an exception if used.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}