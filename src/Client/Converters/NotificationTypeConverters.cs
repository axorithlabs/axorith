using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Axorith.Sdk.Services;

namespace Axorith.Client.Converters;

public class NotificationTypeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NotificationType type)
        {
            return Brushes.Gray;
        }

        return type switch
        {
            NotificationType.Success => new SolidColorBrush(Color.Parse("#10B981")), // Green
            NotificationType.Warning => new SolidColorBrush(Color.Parse("#F59E0B")), // Amber/Yellow
            NotificationType.Error => new SolidColorBrush(Color.Parse("#EF4444")), // Red
            NotificationType.Info => new SolidColorBrush(Color.Parse("#3B82F6")), // Blue
            _ => Brushes.Gray
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
