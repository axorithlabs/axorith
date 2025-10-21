using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;

namespace Axorith.Client.ViewModels;

/// <summary>
/// ViewModel for the session editor view. Manages the creation and modification of session presets.
/// </summary>
public class SessionEditorViewModel : ReactiveObject
{
    private readonly ShellViewModel _shell;
    private readonly IModuleRegistry _moduleRegistry;
    private readonly IPresetManager _presetManager;
    private readonly IServiceProvider _serviceProvider;
    private SessionPreset _preset = new() { Id = Guid.NewGuid() };

    /// <summary>
    /// Gets or sets the preset to be edited. If set to null, a new preset will be created.
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

    /// <summary>
    /// Gets or sets the name of the preset being edited.
    /// </summary>
    private string _name = string.Empty;
    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }

    /// <summary>
    /// Gets the collection of modules that are configured for the current preset.
    /// </summary>
    public ObservableCollection<ConfiguredModuleViewModel> ConfiguredModules { get; } = new();
    
    /// <summary>
    /// Gets or sets the currently selected module in the list of configured modules.
    /// </summary>
    private ConfiguredModuleViewModel? _selectedModule;
    public ConfiguredModuleViewModel? SelectedModule { get => _selectedModule; set => this.RaiseAndSetIfChanged(ref _selectedModule, value); }
    
    /// <summary>
    /// Gets the collection of modules that are available to be added to the preset.
    /// </summary>
    public ObservableCollection<IModule> AvailableModulesToAdd { get; } = new();

    /// <summary>
    /// Gets or sets the module selected in the ComboBox, ready to be added.
    /// </summary>
    private IModule? _moduleToAdd;
    public IModule? ModuleToAdd { get => _moduleToAdd; set => this.RaiseAndSetIfChanged(ref _moduleToAdd, value); }

    /// <summary>
    /// Command to save the preset and close the editor.
    /// </summary>
    public ICommand SaveAndCloseCommand { get; }
    
    /// <summary>
    /// Command to close the editor without saving changes.
    /// </summary>
    public ICommand CancelCommand { get; }
    
    /// <summary>
    /// Command to add the selected module to the preset.
    /// </summary>
    public ICommand AddModuleCommand { get; }
    
    /// <summary>
    /// Command to remove the currently selected module from the preset.
    /// </summary>
    public ICommand RemoveModuleCommand { get; }

    public SessionEditorViewModel(ShellViewModel shell, IModuleRegistry moduleRegistry, IPresetManager presetManager, IServiceProvider serviceProvider)
    {
        _shell = shell;
        _moduleRegistry = moduleRegistry;
        _presetManager = presetManager;
        _serviceProvider = serviceProvider;

        var canSave = this.WhenAnyValue(vm => vm.Name).Select(name => !string.IsNullOrWhiteSpace(name));
        SaveAndCloseCommand = ReactiveCommand.CreateFromTask(SaveAndCloseAsync, canSave);
        CancelCommand = ReactiveCommand.Create(Cancel);
        
        var canAdd = this.WhenAnyValue(vm => vm.ModuleToAdd).Select(m => m != null);
        AddModuleCommand = ReactiveCommand.Create(AddSelectedModule, canAdd);

        var canRemove = this.WhenAnyValue(vm => vm.SelectedModule).Select(selected => selected != null);
        RemoveModuleCommand = ReactiveCommand.Create(() =>
        {
            if (SelectedModule != null)
            {
                _preset.Modules.Remove(SelectedModule.Model);
                ConfiguredModules.Remove(SelectedModule);
                UpdateAvailableModules();
            }
        }, canRemove);
        
        UpdateAvailableModules();
    }

    private void LoadFromPreset()
    {
        Name = _preset.Name;
        ConfiguredModules.Clear();
        foreach (var configured in _preset.Modules)
        {
            var moduleDef = _moduleRegistry.GetDefinitionById(configured.ModuleId);
            if (moduleDef != null)
            {
                ConfiguredModules.Add(new ConfiguredModuleViewModel(moduleDef, configured));
            }
        }
        UpdateAvailableModules();
    }

    private void UpdateAvailableModules()
    {
        AvailableModulesToAdd.Clear();
        var allModules = _moduleRegistry.GetAllDefinitions();
        var configuredModuleIds = new HashSet<Guid>(ConfiguredModules.Select(cm => cm.Module.Id));
        foreach (var module in allModules)
        {
            if (!configuredModuleIds.Contains(module.Id))
            {
                AvailableModulesToAdd.Add(module);
            }
        }
    }

    private void AddSelectedModule()
    {
        if (ModuleToAdd == null) return;
        
        var moduleToAdd = ModuleToAdd;
        var newConfiguredModule = new ConfiguredModule { ModuleId = moduleToAdd.Id };
        _preset.Modules.Add(newConfiguredModule);
        var newVm = new ConfiguredModuleViewModel(moduleToAdd, newConfiguredModule);
        ConfiguredModules.Add(newVm);
        
        UpdateAvailableModules();
        SelectedModule = newVm;
        ModuleToAdd = null;
    }

    private async Task SaveAndCloseAsync()
    {
        foreach (var moduleVm in ConfiguredModules)
        {
            moduleVm.SaveChangesToModel();
        }
        _preset.Name = Name;
        await _presetManager.SavePresetAsync(_preset, CancellationToken.None);
        Cancel();
    }

    private void Cancel()
    {
        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        mainViewModel.LoadPresetsCommand.Execute(Unit.Default);
        _shell.NavigateTo(mainViewModel);
    }
}