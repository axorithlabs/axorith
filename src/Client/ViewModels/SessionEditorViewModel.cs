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

public class SessionEditorViewModel : ReactiveObject
{
    private readonly ShellViewModel _shell;
    private readonly IModulesApi _modulesApi;
    private readonly IPresetsApi _presetsApi;
    private readonly IServiceProvider _serviceProvider;
    private IReadOnlyList<ModuleDefinition> _availableModules = [];
    private SessionPreset _preset = new(id: Guid.NewGuid());

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
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            if (!string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(ErrorMessage))
            {
                ErrorMessage = string.Empty;
            }
            else if (string.IsNullOrWhiteSpace(value))
            {
                ErrorMessage = "Preset name cannot be empty.";
            }
        }
    } = string.Empty;

    public string? ErrorMessage
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<ConfiguredModuleViewModel> ConfiguredModules { get; } = [];

    public ConfiguredModuleViewModel? SelectedModule
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<ModuleDefinition> AvailableModulesToAdd { get; } = [];

    public ModuleDefinition? ModuleToAdd
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ICommand SaveAndCloseCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AddModuleCommand { get; }
    public ICommand RemoveModuleCommand { get; }
    public ICommand OpenModuleSettingsCommand { get; }
    public ICommand CloseModuleSettingsCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

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
            ConfiguredModules.Remove(moduleVm);
            moduleVm.Dispose();
            if (SelectedModule == moduleVm)
            {
                SelectedModule = null;
            }

            UpdateModuleLinks();
        });

        OpenModuleSettingsCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(moduleVm =>
        {
            SelectedModule = moduleVm;
        });

        CloseModuleSettingsCommand = ReactiveCommand.Create(() => { SelectedModule = null; });

        MoveUpCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(vm =>
        {
            var index = ConfiguredModules.IndexOf(vm);
            if (index > 0)
            {
                ConfiguredModules.Move(index, index - 1);
                UpdateModuleLinks();
            }
        });

        MoveDownCommand = ReactiveCommand.Create<ConfiguredModuleViewModel>(vm =>
        {
            var index = ConfiguredModules.IndexOf(vm);
            if (index < ConfiguredModules.Count - 1)
            {
                ConfiguredModules.Move(index, index + 1);
                UpdateModuleLinks();
            }
        });

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
        foreach (var vm in ConfiguredModules)
        {
            vm.Dispose();
        }

        ConfiguredModules.Clear();

        foreach (var configured in _preset.Modules)
        {
            var moduleDef = _availableModules.FirstOrDefault(m => m.Id == configured.ModuleId);
            if (moduleDef != null)
            {
                ConfiguredModules.Add(new ConfiguredModuleViewModel(moduleDef, configured, _modulesApi, _serviceProvider));
            }
        }

        UpdateModuleLinks();
    }

    private void UpdateAvailableModules()
    {
        AvailableModulesToAdd.Clear();
        foreach (var def in _availableModules)
        {
            AvailableModulesToAdd.Add(def);
        }
    }

    private void AddSelectedModule()
    {
        if (ModuleToAdd == null)
        {
            return;
        }

        var defToAdd = ModuleToAdd;
        var newConfiguredModule = new ConfiguredModule { ModuleId = defToAdd.Id };
        var newVm = new ConfiguredModuleViewModel(defToAdd, newConfiguredModule, _modulesApi, _serviceProvider);
        ConfiguredModules.Add(newVm);
        SelectedModule = newVm;
        ModuleToAdd = null;
        UpdateModuleLinks();
    }

    private async Task SaveAndCloseAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Preset name cannot be empty.";
            return;
        }

        ErrorMessage = string.Empty;

        _preset.Modules = ConfiguredModules.Select(vm =>
        {
            vm.SaveChangesToModel();
            return vm.Model;
        }).ToList();

        _preset.Name = Name;

        try
        {
            var existingPreset = await _presetsApi.GetPresetAsync(_preset.Id);
            if (existingPreset != null)
            {
                await _presetsApi.UpdatePresetAsync(_preset);
            }
            else
            {
                await _presetsApi.CreatePresetAsync(_preset);
            }

            Cancel();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save preset: {ex.Message}";
        }
    }

    private void Cancel()
    {
        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        mainViewModel.LoadPresetsCommand.Execute(Unit.Default);
        _shell.NavigateTo(mainViewModel);
    }

    private void UpdateModuleLinks()
    {
        for (var i = 0; i < ConfiguredModules.Count; i++)
        {
            var current = ConfiguredModules[i];
            var next = i < ConfiguredModules.Count - 1 ? ConfiguredModules[i + 1] : null;
            current.NextModule = next;
            current.IsFirst = i == 0;
            current.IsLast = i == ConfiguredModules.Count - 1;
        }
    }
}