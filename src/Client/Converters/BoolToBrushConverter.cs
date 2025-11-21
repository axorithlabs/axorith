using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Axorith.Client.Converters;

/// <summary>
///     Converts a boolean value to a Brush based on a string parameter.
///     Supports both Resource Keys (for theming) and Hex colors.
///     
///     Usage: 
///     ConverterParameter="TrueResourceKey|FalseResourceKey"
///     OR
///     ConverterParameter="#00FF00|#FF0000"
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
        {
            return AvaloniaProperty.UnsetValue;
        }

        var paramStr = parameter as string;
        
        if (string.IsNullOrWhiteSpace(paramStr))
        {
            return AvaloniaProperty.UnsetValue;
        }

        var parts = paramStr.Split('|');
        var trueKey = parts[0];
        var falseKey = parts.Length > 1 ? parts[1] : null;

        var targetKey = boolValue ? trueKey : falseKey;

        if (string.IsNullOrWhiteSpace(targetKey))
        {
            return AvaloniaProperty.UnsetValue;
        }

        if (Application.Current != null && 
            Application.Current.TryGetResource(targetKey, out var resource) && 
            resource is IBrush resourceBrush)
        {
            return resourceBrush;
        }

        return Color.TryParse(targetKey, out var color) ? new SolidColorBrush(color) : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("BoolToBrushConverter is a one-way converter.");
    }
}