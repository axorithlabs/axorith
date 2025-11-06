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
        get => Setting.GetCurrentValueAsObject() as decimal? ?? 0;
        set => Setting.SetValueFromObject(value);
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

    /// <summary>
    ///     A command that can be bound to a button click to trigger the setting's action.
    /// </summary>
    public ICommand ClickCommand { get; }

    public SettingViewModel(ISetting setting)
    {
        Setting = setting;

        ClickCommand = ReactiveCommand.Create(() => { BoolValue = true; });

        // Validate type mapping: ensure ControlType matches ValueType expectations
        if (!IsControlTypeCompatibleWithValueType(setting.ControlType, setting.ValueType))
            throw new InvalidOperationException(
                $"Setting '{setting.Key}' ControlType '{setting.ControlType}' is incompatible with ValueType '{setting.ValueType.Name}'.");

        // Subscribe to reactive properties of the setting to update the UI.
        Setting.Label
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(l => Label = l, ex =>
            {
                /* Ignore errors after module disposal */
            })
            .DisposeWith(_disposables);

        Setting.IsVisible
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(v => IsVisible = v, ex =>
            {
                /* Ignore errors after module disposal */
            })
            .DisposeWith(_disposables);

        Setting.IsReadOnly
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(r => IsReadOnly = r, ex =>
            {
                /* Ignore errors after module disposal */
            })
            .DisposeWith(_disposables);

        // Subscribe to value changes to raise PropertyChanged for the correct UI property.
        Setting.ValueAsObject
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                switch (Setting.ControlType)
                {
                    case SettingControlType.Text:
                    case SettingControlType.TextArea:
                    case SettingControlType.Secret:
                    case SettingControlType.FilePicker:
                    case SettingControlType.DirectoryPicker:
                    case SettingControlType.Choice:
                        this.RaisePropertyChanged(nameof(StringValue));
                        break;
                    case SettingControlType.Checkbox:
                    case SettingControlType.Button:
                        this.RaisePropertyChanged(nameof(BoolValue));
                        break;
                    case SettingControlType.Number:
                        this.RaisePropertyChanged(nameof(DecimalValue));
                        break;
                }
            }, ex =>
            {
                /* Ignore errors after module disposal */
            })
            .DisposeWith(_disposables);

        // Subscribe to choice updates if this is a Choice setting.
        Setting.Choices?
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(c => Choices = c, ex =>
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