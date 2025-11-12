using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;
using Axorith.Client.CoreSdk;
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
    private readonly Guid _moduleInstanceId;
    private readonly IModulesApi _modulesApi;
    private readonly Subject<object?> _numberUpdates = new();

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
        set
        {
            // Send to server, update will come back via broadcast
            _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, value);
        }
    }

    public bool BoolValue
    {
        get => Setting.GetCurrentValueAsObject() as bool? ?? false;
        set
        {
            // Send to server, update will come back via broadcast
            _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, value);
        }
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
            object boxedValue = Setting.ValueType == typeof(int)
                ? (int)Math.Round(value)
                : value;

            // Debounced send to server, update will come back via broadcast
            _numberUpdates.OnNext(boxedValue);
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
        private set
        {
            this.RaiseAndSetIfChanged(ref _choices, value);
            this.RaisePropertyChanged(nameof(SelectedChoice));
        }
    }

    public decimal NumberIncrement => Setting.ValueType == typeof(int) ? 1 : 0.1m;

    public string NumberFormatString => Setting.ValueType == typeof(int) ? "0" : "0.##";

    public KeyValuePair<string, string>? SelectedChoice
    {
        get
        {
            var currentValue = StringValue;
            foreach (var c in Choices)
                if (c.Key == currentValue)
                    return c;
            return null;
        }
        set
        {
            if (value.HasValue)
            {
                StringValue = value.Value.Key;
                this.RaisePropertyChanged(nameof(StringValue));
                this.RaisePropertyChanged(nameof(SelectedChoice));
            }
        }
    }

    /// <summary>
    ///     A command that can be bound to a button click to trigger the setting's action.
    /// </summary>
    public ICommand ClickCommand { get; }

    public SettingViewModel(ISetting setting, Guid moduleInstanceId, IModulesApi modulesApi)
    {
        Setting = setting;
        _moduleInstanceId = moduleInstanceId;
        _modulesApi = modulesApi;

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

        // Debounce numeric updates to avoid flooding server during slider typing/drags
        _numberUpdates
            .Throttle(TimeSpan.FromMilliseconds(75))
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Subscribe(v => { _ = _modulesApi.UpdateSettingAsync(_moduleInstanceId, Setting.Key, v); })
            .DisposeWith(_disposables);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}