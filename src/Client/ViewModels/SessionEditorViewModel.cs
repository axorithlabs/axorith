using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using Axorith.Client.CoreSdk;
using Axorith.Core.Models;
using Axorith.Sdk;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     ViewModel for the session editor view. Manages the creation and modification of session presets.
/// </summary>
public class SessionEditorViewModel : ReactiveObject
{
    private readonly ShellViewModel _shell;
    private readonly IModulesApi _modulesApi;
    private readonly IPresetsApi _presetsApi;
    private readonly IServiceProvider _serviceProvider;
    private IReadOnlyList<ModuleDefinition> _availableModules = [];
    private SessionPreset _preset = new() { Id = Guid.NewGuid() };

    /// <summary>
    ///     Gets or sets the preset to be edited. If set to null, a new preset will be created.
    /// </summary>
    public SessionPreset? PresetToEdit
    {
        get => _preset;
        set
        {
            _preset = value ?? new SessionPreset { Id = Guid.NewGuid() };
            LoadFromPreset();
        }
    }

    public string Name
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    /// <summary>
    ///     Gets the collection of modules that are configured for the current preset.
    /// </summary>
    public ObservableCollection<ConfiguredModuleViewModel> ConfiguredModules { get; } = [];

    public ConfiguredModuleViewModel? SelectedModule
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    ///     Gets the collection of module definitions that are available to be added to the preset.
    /// </summary>
    public ObservableCollection<ModuleDefinition> AvailableModulesToAdd { get; } = [];

    public ModuleDefinition? ModuleToAdd
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    ///     Command to save the preset and close the editor.
    /// </summary>
    public ICommand SaveAndCloseCommand { get; }

    /// <summary>
    ///     Command to close the editor without saving changes.
    /// </summary>
    public ICommand CancelCommand { get; }

    /// <summary>
    ///     Command to add the selected module to the preset.
    /// </summary>
    public ICommand AddModuleCommand { get; }

    /// <summary>
    ///     Command to remove the currently selected module from the preset.
    /// </summary>
    public ICommand RemoveModuleCommand { get; }

    /// <summary>
    ///     Command to open the settings view for a specific module.
    /// </summary>
    public ICommand OpenModuleSettingsCommand { get; }

    /// <summary>
    ///     Command to close the module settings overlay.
    /// </summary>
    public ICommand CloseModuleSettingsCommand { get; }

    public SessionEditorViewModel(ShellViewModel shell, IModulesApi modulesApi, IPresetsApi presetsApi,
        IServiceProvider serviceProvider)
    {
        _shell = shell;
        _modulesApi = modulesApi;
        _presetsApi = presetsApi;
        _serviceProvider = serviceProvider;

        var canSave = this.WhenAnyValue(vm => vm.Name).Select(name => !string.IsNullOrWhiteSpace(name));
        SaveAndCloseCommand = ReactiveCommand.CreateFromTask(SaveAndCloseAsync, canSave);
        CancelCommand = ReactiveCommand.Create(Cancel);

        var canAdd = this.WhenAnyValue(vm => vm.ModuleToAdd).Select(m => m != null);
        AddModuleCommand = ReactiveCommand.Create(AddSelectedModule, canAdd);

        RemoveModuleCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(moduleVm =>
        {
            _preset.Modules.Remove(moduleVm.Model);
            ConfiguredModules.Remove(moduleVm);
            moduleVm.Dispose();
            if (SelectedModule == moduleVm) SelectedModule = null;
        });

        OpenModuleSettingsCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(moduleVm =>
        {
            SelectedModule = moduleVm;
        });

        CloseModuleSettingsCommand = ReactiveCommand.Create(() => { SelectedModule = null; });

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var modules = await _modulesApi.ListModulesAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _availableModules = modules;
                UpdateAvailableModules();

                LoadFromPreset();
            });
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _availableModules = [];
                UpdateAvailableModules();
            });
        }
    }

    private void LoadFromPreset()
    {
        Name = _preset.Name;

        foreach (var vm in ConfiguredModules) vm.Dispose();
        ConfiguredModules.Clear();

        foreach (var configured in _preset.Modules)
        {
            var moduleDef = _availableModules.FirstOrDefault(m => m.Id == configured.ModuleId);
            if (moduleDef != null)
                ConfiguredModules.Add(new ConfiguredModuleViewModel(moduleDef, configured,
                    _modulesApi));
        }
    }

    private void UpdateAvailableModules()
    {
        AvailableModulesToAdd.Clear();
        foreach (var def in _availableModules)
            AvailableModulesToAdd.Add(def);
    }

    private void AddSelectedModule()
    {
        if (ModuleToAdd == null) return;

        var defToAdd = ModuleToAdd;
        var newConfiguredModule = new ConfiguredModule { ModuleId = defToAdd.Id };
        _preset.Modules.Add(newConfiguredModule);

        var newVm = new ConfiguredModuleViewModel(defToAdd, newConfiguredModule, _modulesApi);
        ConfiguredModules.Add(newVm);

        SelectedModule = newVm;
        ModuleToAdd = null;
    }

    private async Task SaveAndCloseAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
            // TODO:
            return;

        foreach (var moduleVm in ConfiguredModules) moduleVm.SaveChangesToModel();

        _preset.Name = Name;

        try
        {
            var existingPreset = await _presetsApi.GetPresetAsync(_preset.Id);
            if (existingPreset != null)
                await _presetsApi.UpdatePresetAsync(_preset);
            else
                await _presetsApi.CreatePresetAsync(_preset);

            Cancel();
        }
        catch (Exception)
        {
            // TODO:
            // ("Save Failed", $"Failed to save preset: {ex.Message}");
        }
    }

    private void Cancel()
    {
        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        mainViewModel.LoadPresetsCommand.Execute(Unit.Default);
        _shell.NavigateTo(mainViewModel);
    }
}