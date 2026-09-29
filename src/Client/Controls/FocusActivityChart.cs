using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Axorith.Client.Controls;

public sealed class FocusActivityChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>> ValuesProperty =
        AvaloniaProperty.Register<FocusActivityChart, IReadOnlyList<double>>(nameof(Values), Array.Empty<double>());

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<FocusActivityChart, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> AxisStrokeProperty =
        AvaloniaProperty.Register<FocusActivityChart, IBrush?>(nameof(AxisStroke));

    public IReadOnlyList<double> Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? AxisStroke
    {
        get => GetValue(AxisStrokeProperty);
        set => SetValue(AxisStrokeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty || change.Property == StrokeProperty ||
            change.Property == AxisStrokeProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        if (AxisStroke is { } axisStroke)
        {
            context.DrawLine(new Pen(axisStroke), new Point(0, height - 1), new Point(width, height - 1));
        }

        var values = Values;
        if (Stroke is not { } stroke || values.Count < 2)
        {
            return;
        }

        var max = values.Max();
        if (max <= 0)
        {
            return;
        }

        var pen = new Pen(stroke, 2);
        var chartHeight = Math.Max(1, height - 12);
        var previous = new Point(0, height - 6 - values[0] / max * chartHeight);
        for (var i = 1; i < values.Count; i++)
        {
            var current = new Point(width * i / (values.Count - 1), height - 6 - values[i] / max * chartHeight);
            context.DrawLine(pen, previous, current);
            previous = current;
        }
    }
}
