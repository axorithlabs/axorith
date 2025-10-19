using Axorith.Core.Models;
using Axorith.Sdk;
using ReactiveUI;
using System.Collections.ObjectModel;

namespace Axorith.Client.ViewModels;

/// <summary>
/// Represents a single module that has been added to a session preset in the editor.
/// This ViewModel wraps the module's definition (IModule) and its configured data (ConfiguredModule).
/// </summary>
public class ConfiguredModuleViewModel : ReactiveObject
{
    /// <summary>
    /// Gets the underlying module definition from the SDK.
    /// </summary>
    public IModule Module { get; }

    /// <summary>
    /// Gets the data model that holds the user-configured settings for this module instance.
    /// </summary>
    public ConfiguredModule Model { get; }

    /// <summary>
    /// Gets the display name of the module.
    /// </summary>
    public string Name => Module.Name;

    /// <summary>
    /// Gets a collection of ViewModels for each of the module's settings.
    /// </summary>
    public ObservableCollection<SettingViewModel> Settings { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfiguredModuleViewModel"/> class.
    /// </summary>
    /// <param name="module">The module definition.</param>
    /// <param name="model">The data model containing saved settings for this module.</param>
    public ConfiguredModuleViewModel(IModule module, ConfiguredModule model)
    {
        Module = module;
        Model = model;
        foreach (var settingDef in module.GetSettings())
        {
            // The SettingViewModel's constructor will handle loading the correct value.
            Settings.Add(new SettingViewModel(settingDef, Model.Settings));
        }
    }

    /// <summary>
    /// Persists the current UI values from the setting ViewModels back into the data Model.
    /// This is called before saving the preset.
    /// </summary>
    public void SaveChangesToModel()
    {
        Model.Settings.Clear();
        foreach (var settingVm in Settings)
        {
            // Each setting definition knows how to format its value for saving.
            Model.Settings[settingVm.Setting.Key] = settingVm.Setting.GetValueFromViewModel(settingVm);
        }
    }
}