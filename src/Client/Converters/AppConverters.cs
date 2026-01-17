using Avalonia.Data.Converters;
using Axorith.Client.ViewModels;

namespace Axorith.Client.Converters;

/// <summary>
///     Provides static utility converters for the application.
/// </summary>
public static class AppConverters
{
    /// <summary>
    ///     Returns true if the value is a StopAtTimeTriggerViewModel.
    /// </summary>
    public static readonly IValueConverter IsStopAtTimeTrigger =
        new FuncValueConverter<object?, bool>(value => value is StopAtTimeTriggerViewModel);

    /// <summary>
    ///     Returns true if the value is a StopAfterDurationTriggerViewModel.
    /// </summary>
    public static readonly IValueConverter IsStopAfterDurationTrigger =
        new FuncValueConverter<object?, bool>(value => value is StopAfterDurationTriggerViewModel);

    /// <summary>
    ///     Returns true if the value is a ThenStartAnotherTriggerViewModel.
    /// </summary>
    public static readonly IValueConverter IsThenStartAnotherTrigger =
        new FuncValueConverter<object?, bool>(value => value is ThenStartAnotherTriggerViewModel);

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

    /// <summary>
    ///     Converts bool (is24HourFormat) to minimum hour value: 0 for 24-hour, 1 for 12-hour.
    /// </summary>
    public static readonly IValueConverter BoolToMinHour =
        new FuncValueConverter<bool, int>(is24Hour => is24Hour ? 0 : 1);

    /// <summary>
    ///     Converts bool (is24HourFormat) to maximum hour value: 23 for 24-hour, 12 for 12-hour.
    /// </summary>
    public static readonly IValueConverter BoolToMaxHour =
        new FuncValueConverter<bool, int>(is24Hour => is24Hour ? 23 : 12);

    public static readonly IValueConverter BoolToRunningText =
        new FuncValueConverter<bool, string>(isRunning => isRunning ? "Running..." : "Run Setup");

    public static readonly IValueConverter BoolToSetupText =
        new FuncValueConverter<bool, string>(isRunning => isRunning ? "Running..." : "Run Setup Wizard");
}