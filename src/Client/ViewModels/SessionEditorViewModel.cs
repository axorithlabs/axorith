using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
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

    /// <summary>
    ///     Gets or sets the name of the preset being edited.
    /// </summary>
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    /// <summary>
    ///     Gets the collection of modules that are configured for the current preset.
    /// </summary>
    public ObservableCollection<ConfiguredModuleViewModel> ConfiguredModules { get; } = [];

    /// <summary>
    ///     Gets or sets the currently selected module in the list of configured modules.
    /// </summary>
    private ConfiguredModuleViewModel? _selectedModule;

    public ConfiguredModuleViewModel? SelectedModule
    {
        get => _selectedModule;
        set => this.RaiseAndSetIfChanged(ref _selectedModule, value);
    }

    /// <summary>
    ///     Gets the collection of module definitions that are available to be added to the preset.
    /// </summary>
    public ObservableCollection<ModuleDefinition> AvailableModulesToAdd { get; } = [];

    /// <summary>
    ///     Gets or sets the module definition selected in the ComboBox, ready to be added.
    /// </summary>
    private ModuleDefinition? _moduleToAdd;

    public ModuleDefinition? ModuleToAdd
    {
        get => _moduleToAdd;
        set => this.RaiseAndSetIfChanged(ref _moduleToAdd, value);
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
        _modulesApi = modulesApi ?? throw new ArgumentNullException(nameof(modulesApi));
        _presetsApi = presetsApi ?? throw new ArgumentNullException(nameof(presetsApi));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

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

                // Re-populate configured modules once definitions are available
                LoadFromPreset();
            });
        }
        catch (Exception)
        {
            // Surface error via shell status (optional) – fallback to no modules
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
        // Validate preset name
        if (string.IsNullOrWhiteSpace(Name))
        {
            await ShowErrorDialogAsync("Preset name is required.", "Please enter a name for this preset.");
            return;
        }

        // Save all module settings to models
        foreach (var moduleVm in ConfiguredModules) moduleVm.SaveChangesToModel();

        // Module validation happens when session starts, not here

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
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Save Failed", $"Failed to save preset: {ex.Message}");
        }
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        var window = Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null) return;

        Window? dialog = null;
        var dialog1 = dialog;
        dialog = new Window
        {
            Title = title,
            Width = 450,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 15,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 18,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = new SolidColorBrush(Colors.OrangeRed)
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 10),
                        MaxWidth = 400
                    },
                    new Button
                    {
                        Content = "OK",
                        Width = 100,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Command = ReactiveCommand.Create(() => dialog1!.Close())
                    }
                }
            }
        };

        await dialog.ShowDialog(window);
    }

    private void Cancel()
    {
        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        mainViewModel.LoadPresetsCommand.Execute(Unit.Default);
        _shell.NavigateTo(mainViewModel);
    }
}