using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.Versioning;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Axorith.Client.Converters;

public sealed class ApplicationChoiceTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text)
            return string.Empty;

        var lines = text.Split('\n');
        var index = string.Equals(parameter?.ToString(), "path", StringComparison.Ordinal) ? 1 : 0;
        return lines.Length > index ? lines[index].TrimEnd('\r') : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ApplicationIconConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Bitmap> Icons = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!OperatingSystem.IsWindows() || value is not string text)
            return null;

        var lines = text.Split('\n');
        var path = lines.Length > 2 ? lines[2].TrimEnd('\r') : lines.ElementAtOrDefault(1)?.TrimEnd('\r');
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return Icons.GetOrAdd(path, ExtractIcon);
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    [SupportedOSPlatform("windows")]
    private static Bitmap ExtractIcon(string path)
    {
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path)
                         ?? throw new InvalidOperationException("No associated icon.");
        using var source = icon.ToBitmap();
        using var stream = new MemoryStream();
        source.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
