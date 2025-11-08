using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using Axorith.Client.Adapters;
using Axorith.Client.CoreSdk;
using Axorith.Core.Models;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     ViewModel for a module instance configured within a session preset.
///     Manages settings and actions retrieved from the Host via gRPC API.
/// </summary>
public class ConfiguredModuleViewModel : ReactiveObject, IDisposable
{
    private readonly IModulesApi _modulesApi;
    private bool _isLoading;

    public ModuleDefinition Definition { get; }
    public ConfiguredModule Model { get; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Model.CustomName) ? Model.CustomName : Definition.Name;

    public string? CustomName
    {
        get => Model.CustomName;
        set
        {
            Model.CustomName = value;
            this.RaisePropertyChanged(nameof(DisplayName));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public ObservableCollection<SettingViewModel> Settings { get; } = [];
    public ObservableCollection<ActionViewModel> Actions { get; } = [];

    public ConfiguredModuleViewModel(ModuleDefinition definition, ConfiguredModule model,
        IReadOnlyList<ModuleDefinition> availableModules, IModulesApi modulesApi)
    {
        Definition = definition;
        Model = model;
        _modulesApi = modulesApi ?? throw new ArgumentNullException(nameof(modulesApi));

        _ = LoadSettingsAndActionsAsync();
    }

    private async Task LoadSettingsAndActionsAsync()
    {
        try
        {
            IsLoading = true;
            var settingsInfo = await _modulesApi.GetModuleSettingsAsync(Definition.Id).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Settings.Clear();
                Actions.Clear();

                // Populate settings with saved values
                foreach (var setting in settingsInfo.Settings)
                {
                    var savedValue = Model.Settings.TryGetValue(setting.Key, out var value) ? value : null;
                    var adaptedSetting = new ModuleSettingAdapter(setting, savedValue);
                    var vm = new SettingViewModel(adaptedSetting);
                    Settings.Add(vm);
                }

                // Populate actions
                foreach (var action in settingsInfo.Actions)
                {
                    var adaptedAction = new ModuleActionAdapter(action, _modulesApi, Definition.Id);
                    Actions.Add(new ActionViewModel(adaptedAction));
                }
            });
        }
        catch (Exception ex)
        {
            // Log error but don't crash - UI will show empty settings
            Debug.WriteLine($"Failed to load module settings: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SaveChangesToModel()
    {
        foreach (var settingVm in Settings)
        {
            if (settingVm.Setting.Persistence != SettingPersistence.Persisted)
                continue;

            Model.Settings[settingVm.Setting.Key] = settingVm.Setting.GetValueAsString();
        }
    }

    public void Dispose()
    {
        foreach (var setting in Settings)
            setting.Dispose();
        Settings.Clear();

        foreach (var action in Actions)
            action.Dispose();
        Actions.Clear();

        GC.SuppressFinalize(this);
    }
}