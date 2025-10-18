using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Axorith.Client.ViewModels;

public class SessionEditorViewModel : ReactiveObject
{
    private readonly ShellViewModel _shell;
    private readonly IModuleRegistry _moduleRegistry;
    private readonly IPresetManager _presetManager;
    private readonly IServiceProvider _serviceProvider;
    private SessionPreset _preset = new() { Id = Guid.NewGuid() };

    public SessionPreset? PresetToEdit
    {
        get => _preset;
        set
        {
            _preset = value ?? new SessionPreset { Id = Guid.NewGuid() };
            LoadFromPreset();
        }
    }

    private string _name = string.Empty;
    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }

    public ObservableCollection<ConfiguredModuleViewModel> ConfiguredModules { get; } = new();
    
    private ConfiguredModuleViewModel? _selectedModule;
    public ConfiguredModuleViewModel? SelectedModule { get => _selectedModule; set => this.RaiseAndSetIfChanged(ref _selectedModule, value); }
    
    public ObservableCollection<IModule> AvailableModulesToAdd { get; } = new();

    private IModule? _moduleToAdd;
    public IModule? ModuleToAdd { get => _moduleToAdd; set => this.RaiseAndSetIfChanged(ref _moduleToAdd, value); }

    public ICommand SaveAndCloseCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AddModuleCommand { get; }
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
            var moduleDef = _moduleRegistry.GetModuleById(configured.ModuleId);
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
        var allModules = _moduleRegistry.GetAllModules();
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