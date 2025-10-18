using Axorith.Sdk;
using ReactiveUI;
using System.Globalization;

namespace Axorith.Client.ViewModels;

public class SettingViewModel(ModuleSetting setting, string currentValue) : ReactiveObject
{
    public ModuleSetting Setting { get; } = setting;

    private string _value = currentValue;

    public string StringValue
    {
        get => _value;
        set
        {
            this.RaiseAndSetIfChanged(ref _value, value);
            this.RaisePropertyChanged(nameof(BoolValue));
            this.RaisePropertyChanged(nameof(DecimalValue));
        }
    }

    public bool BoolValue
    {
        get => bool.TryParse(_value, out var b) && b;
        set => StringValue = value.ToString();
    }

    public decimal DecimalValue
    {
        get => decimal.TryParse(_value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
        set => StringValue = value.ToString(CultureInfo.InvariantCulture);
    }
}