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

public class NotificationTypeToIconConverter : IValueConverter
{
    // Icons (Material Design style paths)
    private static readonly StreamGeometry SuccessIcon = StreamGeometry.Parse(
        "M12 2C6.5 2 2 6.5 2 12S6.5 22 12 22 22 17.5 22 12 17.5 2 12 2M10 17L5 12L6.41 10.59L10 14.17L17.59 6.58L19 8L10 17Z");

    private static readonly StreamGeometry WarningIcon =
        StreamGeometry.Parse("M13 14H11V9H13M13 18H11V16H13M1 21H23L12 2L1 21Z");

    private static readonly StreamGeometry ErrorIcon = StreamGeometry.Parse(
        "M12 2C17.53 2 22 6.47 22 12C22 17.53 17.53 22 12 22C6.47 22 2 17.53 2 12C2 6.47 6.47 2 12 2M15.59 7L12 10.59L8.41 7L7 8.41L10.59 12L7 15.59L8.41 17L12 13.41L15.59 17L17 15.59L13.41 12L17 8.41L15.59 7Z");

    private static readonly StreamGeometry InfoIcon = StreamGeometry.Parse(
        "M11,9H13V7H11M12,20C7.59,20 4,16.41 4,12C4,7.59 7.59,4 12,4C16.41,4 20,7.59 20,12C20,16.41 16.41,20 12,20M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,17H13V11H11V17Z");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NotificationType type)
        {
            return InfoIcon;
        }

        return type switch
        {
            NotificationType.Success => SuccessIcon,
            NotificationType.Warning => WarningIcon,
            NotificationType.Error => ErrorIcon,
            _ => InfoIcon
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}