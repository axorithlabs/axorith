using System.Collections.ObjectModel;
using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

public class ConfiguredModuleViewModel : ReactiveObject, IDisposable
{
    private readonly IModule? _liveInstance;
    private readonly ILifetimeScope? _scope;
    private readonly CancellationTokenSource _initCts = new();
    private Task? _initializationTask;

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

    public ObservableCollection<SettingViewModel> Settings { get; } = [];
    public ObservableCollection<ActionViewModel> Actions { get; } = [];

    public ConfiguredModuleViewModel(ModuleDefinition definition, ConfiguredModule model,
        IModuleRegistry moduleRegistry)
    {
        Definition = definition;
        Model = model;

        (_liveInstance, _scope) = moduleRegistry.CreateInstance(definition.Id);
        if (_liveInstance is null) return;

        var moduleSettings = _liveInstance.GetSettings();

        // Populate the settings with saved values before creating the ViewModels.
        foreach (var setting in moduleSettings)
        {
            model.Settings.TryGetValue(setting.Key, out var savedValue);
            setting.SetValueFromString(savedValue);
        }

        foreach (var setting in moduleSettings) Settings.Add(new SettingViewModel(setting));

        // Load actions (non-persisted)
        foreach (var action in _liveInstance.GetActions()) Actions.Add(new ActionViewModel(action));

        // Initialize heavy resources asynchronously (design-time discovery)
        _initializationTask = InitializeModuleAsync();
    }

    private async Task InitializeModuleAsync()
    {
        if (_liveInstance == null) return;

        try
        {
            await _liveInstance.InitializeAsync(_initCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
        catch (Exception)
        {
            // Errors during initialization are logged by the module itself
        }
    }

    public void SaveChangesToModel()
    {
        // This is a non-destructive save. We only update values, we don't clear the dictionary.
        // Only Persisted settings are saved to the preset JSON.
        // Ephemeral settings (including secrets) are never persisted to disk.
        foreach (var settingVm in Settings)
        {
            if (settingVm.Setting.Persistence != SettingPersistence.Persisted)
                continue;

            Model.Settings[settingVm.Setting.Key] = settingVm.Setting.GetValueAsString();
        }
    }

    public void Dispose()
    {
        // Dispose ViewModels FIRST to unsubscribe from observables
        foreach (var setting in Settings) setting.Dispose();
        Settings.Clear();

        foreach (var action in Actions) action.Dispose();
        Actions.Clear();

        // THEN cancel and dispose module
        _initCts.Cancel();
        _initCts.Dispose();

        // Don't wait for initialization to complete - just cancel it
        _liveInstance?.Dispose();
        _scope?.Dispose();

        GC.SuppressFinalize(this);
    }
}