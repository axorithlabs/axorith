using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Axorith.Sdk.Services;

namespace Axorith.Client.Converters;

public class NotificationTypeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            NotificationType.Success => "SuccessPrimaryBrush",
            NotificationType.Warning => "WarningPrimaryBrush",
            NotificationType.Error => "ErrorPrimaryBrush",
            NotificationType.Info => "AccentInfoBrush",
            _ => "TextMutedBrush"
        };

        return Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var brush)
            ? brush!
            : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
