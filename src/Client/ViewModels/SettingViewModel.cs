using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows.Input;
using Axorith.Sdk.Settings;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     ViewModel for a single reactive module setting.
///     It acts as a bridge between the reactive ISetting from the SDK and the Avalonia UI controls.
/// </summary>
public class SettingViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    /// <summary>
    ///     Gets the underlying reactive setting definition from the SDK.
    /// </summary>
    public ISetting Setting { get; }

    private string _label = string.Empty;

    public string Label
    {
        get => _label;
        private set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    private bool _isVisible = true;

    public bool IsVisible
    {
        get => _isVisible;
        private set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    private bool _isReadOnly;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        private set => this.RaiseAndSetIfChanged(ref _isReadOnly, value);
    }

    // --- Value Properties for UI Binding ---

    public string StringValue
    {
        get => Setting.GetCurrentValueAsObject() as string ?? string.Empty;
        set => Setting.SetValueFromString(value);
    }

    public bool BoolValue
    {
        get => Setting.GetCurrentValueAsObject() as bool? ?? false;
        set => Setting.SetValueFromObject(value);
    }

    public decimal DecimalValue
    {
        get
        {
            var value = Setting.GetCurrentValueAsObject();
            if (value == null)
                // Return 0 as safe default for empty values
                return 0;

            // Handle different numeric types
            return value switch
            {
                decimal d => d,
                int i => i,
                double db => (decimal)db,
                TimeSpan ts => (decimal)ts.TotalSeconds,
                IConvertible c => Convert.ToDecimal(c),
                _ => 0
            };
        }
        set
        {
            // For int settings, round the value
            if (Setting.ValueType == typeof(int))
                Setting.SetValueFromObject((int)Math.Round(value));
            else
                Setting.SetValueFromObject(value);
        }
    }

    // Helper to support TimeSpan serialization via seconds when saving
    public static TimeSpan TimeSpanFromDecimal(decimal value)
    {
        return TimeSpan.FromSeconds((double)value);
    }

    private IReadOnlyList<KeyValuePair<string, string>> _choices = [];

    public IReadOnlyList<KeyValuePair<string, string>> Choices
    {
        get => _choices;
        private set => this.RaiseAndSetIfChanged(ref _choices, value);
    }

    public decimal NumberIncrement => Setting.ValueType == typeof(int) ? 1 : 0.1m;

    public string NumberFormatString => Setting.ValueType == typeof(int) ? "0" : "0.##";

    public KeyValuePair<string, string>? SelectedChoice
    {
        get
        {
            var currentValue = StringValue;
            return Choices.FirstOrDefault(c => c.Key == currentValue);
        }
        set
        {
            if (value.HasValue) StringValue = value.Value.Key;
        }
    }

    /// <summary>
    ///     A command that can be bound to a button click to trigger the setting's action.
    /// </summary>
    public ICommand ClickCommand { get; }

    public SettingViewModel(ISetting setting)
    {
        Setting = setting;

        ClickCommand = ReactiveCommand.Create(() => { BoolValue = true; });

        // Subscribe to label updates
        setting.Label
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(newLabel => Label = newLabel)
            .DisposeWith(_disposables);

        // Subscribe to visibility updates
        setting.IsVisible
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(visible => IsVisible = visible)
            .DisposeWith(_disposables);

        // Subscribe to readonly updates
        setting.IsReadOnly
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(readOnly => IsReadOnly = readOnly)
            .DisposeWith(_disposables);

        // Subscribe to value changes and raise property changed for appropriate properties
        setting.ValueAsObject
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                // Notify all value properties - the UI will decide which one to use
                this.RaisePropertyChanged(nameof(StringValue));
                this.RaisePropertyChanged(nameof(BoolValue));
                this.RaisePropertyChanged(nameof(DecimalValue));
                this.RaisePropertyChanged(nameof(SelectedChoice));
            }, _ =>
            {
                /* Ignore errors after module disposal */
            })
            .DisposeWith(_disposables);

        // CRITICAL: Raise initial property changed for value bindings to show saved values
        this.RaisePropertyChanged(nameof(StringValue));
        this.RaisePropertyChanged(nameof(BoolValue));
        this.RaisePropertyChanged(nameof(DecimalValue));
        this.RaisePropertyChanged(nameof(SelectedChoice));

        // Subscribe to choice updates if this is a Choice setting.
        Setting.Choices?
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(c => Choices = c, _ =>
            {
                /* Ignore errors after module disposal */
            })
            .DisposeWith(_disposables);
    }

    private static bool IsControlTypeCompatibleWithValueType(SettingControlType controlType, Type valueType)
    {
        return controlType switch
        {
            SettingControlType.Text or SettingControlType.TextArea or SettingControlType.Secret or
                SettingControlType.FilePicker or SettingControlType.DirectoryPicker or SettingControlType.Choice
                => valueType == typeof(string),
            SettingControlType.Checkbox or SettingControlType.Button => valueType == typeof(bool),
            SettingControlType.Number => valueType == typeof(decimal) || valueType == typeof(int) ||
                                         valueType == typeof(double) || valueType == typeof(TimeSpan),
            _ => true
        };
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}