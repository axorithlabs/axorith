using System.Collections.ObjectModel;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ConfiguredModuleViewModel : ReactiveObject
{
    public ModuleDefinition Definition { get; }
    public ConfiguredModule Model { get; }

    /// <summary>
    ///     The display name for this instance. Uses CustomName if available, otherwise falls back to the module's default
    ///     name.
    /// </summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(Model.CustomName) ? Model.CustomName : Definition.Name;

    /// <summary>
    ///     The user-editable custom name for this instance.
    /// </summary>
    public string? CustomName
    {
        get => Model.CustomName;
        set
        {
            Model.CustomName = value;
            this.RaisePropertyChanged(nameof(DisplayName));
        }
    }

    public ObservableCollection<SettingViewModel> Settings { get; } = new();

    public ConfiguredModuleViewModel(ModuleDefinition definition, ConfiguredModule model,
        IModuleRegistry moduleRegistry)
    {
        Definition = definition;
        Model = model;

        using var tempInstance = moduleRegistry.CreateInstance(definition.Id).Instance;
        if (tempInstance != null)
            foreach (var settingDef in tempInstance.GetSettings())
                Settings.Add(new SettingViewModel(settingDef, Model.Settings));
    }

    public void SaveChangesToModel()
    {
        Model.Settings.Clear();
        foreach (var settingVm in Settings)
            Model.Settings[settingVm.Setting.Key] = settingVm.Setting.GetValueFromViewModel(settingVm);
    }
}