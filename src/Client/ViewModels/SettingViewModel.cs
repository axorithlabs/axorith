using Axorith.Sdk.Settings;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
/// ViewModel for a single module setting, acting as a bridge between the SDK's setting definitions and the UI controls.
/// Implements <see cref="ISettingViewModel"/> to allow setting definitions to populate it without casting.
/// </summary>
public class SettingViewModel : ReactiveObject, ISettingViewModel
{
    /// <summary>
    /// Gets the underlying setting definition from the SDK.
    /// </summary>
    public SettingBase Setting { get; }

    private string _stringValue = string.Empty;
    /// <summary>
    /// The value for text-based settings. Bound to TextBox controls.
    /// </summary>
    public string StringValue { get => _stringValue; set => this.RaiseAndSetIfChanged(ref _stringValue, value); }

    private bool _boolValue;
    /// <summary>
    /// The value for boolean settings. Bound to CheckBox controls.
    /// </summary>
    public bool BoolValue { get => _boolValue; set => this.RaiseAndSetIfChanged(ref _boolValue, value); }

    private decimal _decimalValue;
    /// <summary>
    /// The value for numeric settings. Bound to NumericUpDown controls.
    /// </summary>
    public decimal DecimalValue { get => _decimalValue; set => this.RaiseAndSetIfChanged(ref _decimalValue, value); }

    private IReadOnlyList<KeyValuePair<string, string>> _choicesValue = [];
    /// <summary>
    /// Gets the collection of choices for a ChoiceSetting.
    /// This is exposed for binding to a ComboBox's ItemsSource.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> ChoicesValue { get => _choicesValue; set => this.RaiseAndSetIfChanged(ref _choicesValue, value); }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingViewModel"/> class.
    /// </summary>
    /// <param name="setting">The setting definition from the SDK.</param>
    /// <param name="savedSettings">The dictionary of all saved string values for the parent module.</param>
    public SettingViewModel(SettingBase setting, IReadOnlyDictionary<string, string> savedSettings)
    {
        Setting = setting;
        savedSettings.TryGetValue(setting.Key, out var savedValue);
        
        // The setting definition itself is responsible for parsing the saved value
        // and populating the correct property on this ViewModel.
        setting.InitializeViewModel(this, savedValue);
    }
}