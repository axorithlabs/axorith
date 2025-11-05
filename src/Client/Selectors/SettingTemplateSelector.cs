using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Axorith.Client.ViewModels;
using Axorith.Sdk.Settings;

namespace Axorith.Client.Selectors;

/// <summary>
///     A data template selector that chooses the correct UI control for a given SettingViewModel.
///     This is the robust, code-based alternative to using style selectors.
/// </summary>
public class SettingTemplateSelector : IDataTemplate
{
    public IDataTemplate? TextTemplate { get; set; }
    public IDataTemplate? TextAreaTemplate { get; set; }
    public IDataTemplate? SecretTemplate { get; set; }
    public IDataTemplate? CheckboxTemplate { get; set; }
    public IDataTemplate? NumberTemplate { get; set; }
    public IDataTemplate? ChoiceTemplate { get; set; }
    public IDataTemplate? FilePickerTemplate { get; set; }
    public IDataTemplate? DirectoryPickerTemplate { get; set; }
    public IDataTemplate? ButtonTemplate { get; set; }

    /// <summary>
    ///     This method is called by Avalonia to build the UI for an item.
    /// </summary>
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
            SettingControlType.FilePicker => FilePickerTemplate,
            SettingControlType.DirectoryPicker => DirectoryPickerTemplate,
            SettingControlType.Button => ButtonTemplate,
            _ => null
        };

        return template?.Build(data) ?? new TextBlock { Text = $"ERROR: No template for {data?.GetType().Name}" };
    }

    /// <summary>
    ///     This method tells Avalonia if this selector can handle the given data.
    /// </summary>
    public bool Match(object? data)
    {
        return data is SettingViewModel;
    }
}