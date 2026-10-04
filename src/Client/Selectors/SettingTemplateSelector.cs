using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Axorith.Client.ViewModels;
using Axorith.Sdk.Settings;

namespace Axorith.Client.Selectors;

public class SettingTemplateSelector : IDataTemplate
{
    public IDataTemplate? TextTemplate { get; set; }
    public IDataTemplate? TextAreaTemplate { get; set; }
    public IDataTemplate? SecretTemplate { get; set; }
    public IDataTemplate? CheckboxTemplate { get; set; }
    public IDataTemplate? NumberTemplate { get; set; }
    public IDataTemplate? ChoiceTemplate { get; set; }
    public IDataTemplate? MultiChoiceTemplate { get; set; }
    public IDataTemplate? PathPickerTemplate { get; set; }
    public IDataTemplate? ButtonTemplate { get; set; }

    public Control Build(object? data)
    {
        var vm = data as SettingViewModel;

        var template = vm?.Setting.ControlType switch
        {
            SettingControlType.Secret => SecretTemplate,
            SettingControlType.Text => TextTemplate,
            SettingControlType.TextArea => TextAreaTemplate,
            SettingControlType.Checkbox => CheckboxTemplate,
            SettingControlType.Number => NumberTemplate,
            SettingControlType.Choice => ChoiceTemplate,
            SettingControlType.MultiChoice => MultiChoiceTemplate,
            SettingControlType.FilePicker or SettingControlType.DirectoryPicker => PathPickerTemplate,
            SettingControlType.Button => ButtonTemplate,
            _ => null
        };

        return template?.Build(data) ?? new TextBlock { Text = $"ERROR: No template for {data?.GetType().Name}" };
    }

    public bool Match(object? data) => data is SettingViewModel;
}
