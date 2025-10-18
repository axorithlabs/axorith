using Axorith.Core.Models;
using Axorith.Sdk;
using ReactiveUI;
using System.Collections.ObjectModel;

namespace Axorith.Client.ViewModels;

public class ConfiguredModuleViewModel : ReactiveObject
{
    public IModule Module { get; }
    public ConfiguredModule Model { get; }
    public string Name => Module.Name;
    public ObservableCollection<SettingViewModel> Settings { get; } = new();

    public ConfiguredModuleViewModel(IModule module, ConfiguredModule model)
    {
        Module = module;
        Model = model;
        foreach (var settingDef in Module.GetSettings())
        {
            var currentValue = Model.Settings.GetValueOrDefault(settingDef.Key, settingDef.DefaultValue);
            Settings.Add(new SettingViewModel(settingDef, currentValue));
        }
    }

    public void SaveChangesToModel()
    {
        Model.Settings.Clear();
        foreach (var settingVm in Settings)
        {
            Model.Settings[settingVm.Setting.Key] = settingVm.StringValue;
        }
    }
}