using Avalonia.Data.Converters;
using Axorith.Client.ViewModels;

namespace Axorith.Client.Converters;

public static class AppConverters
{
    public static readonly IValueConverter IsStopAtTimeTrigger =
        new FuncValueConverter<object?, bool>(value => value is StopAtTimeTriggerViewModel);

    public static readonly IValueConverter IsStopAfterDurationTrigger =
        new FuncValueConverter<object?, bool>(value => value is StopAfterDurationTriggerViewModel);

    public static readonly IValueConverter BoolToMinHour =
        new FuncValueConverter<bool, int>(is24Hour => is24Hour ? 0 : 1);

    public static readonly IValueConverter BoolToMaxHour =
        new FuncValueConverter<bool, int>(is24Hour => is24Hour ? 23 : 12);

}
