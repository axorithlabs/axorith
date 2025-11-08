using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Axorith.Client.CoreSdk;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     ViewModel for the main dashboard view. Manages the list of presets and session lifecycle commands.
/// </summary>
public class MainViewModel : ReactiveObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly IPresetsApi _presetsApi;
    private readonly ISessionsApi _sessionsApi;
    private readonly IServiceProvider _serviceProvider;
    private readonly CompositeDisposable _disposables = new();

    private SessionPresetViewModel? _selectedPreset;

    /// <summary>
    ///     The currently selected session preset in the list.
    /// </summary>
    public SessionPresetViewModel? SelectedPreset
    {
        get => _selectedPreset;
        set => this.RaiseAndSetIfChanged(ref _selectedPreset, value);
    }

    private Guid? _activeSessionPresetId;

    public Guid? ActiveSessionPresetId
    {
        get => _activeSessionPresetId;
        private set => this.RaiseAndSetIfChanged(ref _activeSessionPresetId, value);
    }

    private string _sessionStatus = "No session is active.";

    /// <summary>
    ///     A user-friendly string describing the current session status.
    /// </summary>
    public string SessionStatus
    {
        get => _sessionStatus;
        set => this.RaiseAndSetIfChanged(ref _sessionStatus, value);
    }

    private bool _isSessionActive;

    /// <summary>
    ///     Gets a value indicating whether a session is currently active.
    ///     This property is exposed for binding in the View.
    /// </summary>
    public bool IsSessionActive
    {
        get => _isSessionActive;
        private set => this.RaiseAndSetIfChanged(ref _isSessionActive, value);
    }

    /// <summary>
    ///     A collection of all available session presets loaded from the core.
    /// </summary>
    public ObservableCollection<SessionPresetViewModel> Presets { get; } = [];

    /// <summary>
    ///     Command to delete the currently selected preset.
    /// </summary>
    public ICommand DeleteSelectedCommand { get; }

    /// <summary>
    ///     Command to open the editor for the currently selected preset.
    /// </summary>
    public ICommand EditSelectedCommand { get; }

    /// <summary>
    ///     Command to start a session with the currently selected preset.
    /// </summary>
    public ICommand StartSelectedCommand { get; }

    /// <summary>
    ///     Command to stop the currently active session.
    /// </summary>
    public ICommand StopSessionCommand { get; }

    /// <summary>
    ///     Command to reload the list of presets from storage.
    /// </summary>
    public ICommand LoadPresetsCommand { get; }

    /// <summary>
    ///     Command to open the editor to create a new preset.
    /// </summary>
    public ICommand CreateSessionCommand { get; }


    public MainViewModel(ShellViewModel shell, IPresetsApi presetsApi, ISessionsApi sessionsApi,
        IServiceProvider serviceProvider)
    {
        _shell = shell;
        _presetsApi = presetsApi;
        _sessionsApi = sessionsApi;
        _serviceProvider = serviceProvider;

        // Subscribe to session events via Observable
        var subscription = _sessionsApi.SessionEvents
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(evt =>
            {
                switch (evt.Type)
                {
                    case SessionEventType.Started:
                        SessionStatus = "Session is active.";
                        ActiveSessionPresetId = evt.PresetId;
                        IsSessionActive = true;
                        break;
                    case SessionEventType.Stopped:
                        SessionStatus = "No session is active.";
                        ActiveSessionPresetId = null;
                        IsSessionActive = false;
                        break;
                }
            });

        _disposables.Add(subscription);

        // Ensure canExecute streams update commands on UI thread
        var canManipulatePreset = this
            .WhenAnyValue(vm => vm.IsSessionActive, isActive => !isActive)
            .ObserveOn(RxApp.MainThreadScheduler);
        var canStopSession = this
            .WhenAnyValue(vm => vm.IsSessionActive)
            .ObserveOn(RxApp.MainThreadScheduler);
        var canDoGlobalActions = this
            .WhenAnyValue(vm => vm.IsSessionActive, isActive => !isActive)
            .ObserveOn(RxApp.MainThreadScheduler);

        DeleteSelectedCommand =
            ReactiveCommand.CreateFromTask<SessionPresetViewModel>(DeletePresetAsync, canManipulatePreset);
        EditSelectedCommand = ReactiveCommand.Create<SessionPresetViewModel>(EditPreset, canManipulatePreset);
        StartSelectedCommand =
            ReactiveCommand.CreateFromTask<SessionPresetViewModel>(StartPresetAsync, canManipulatePreset);
        StopSessionCommand = ReactiveCommand.CreateFromTask(StopCurrentSessionAsync, canStopSession);
        LoadPresetsCommand = ReactiveCommand.CreateFromTask(LoadPresetsAsync, canDoGlobalActions);
        CreateSessionCommand = ReactiveCommand.Create(CreateNewSession, canDoGlobalActions);
    }

    /// <summary>
    ///     Asynchronously initializes the ViewModel by loading the initial list of presets.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadPresetsAsync();
        await RefreshSessionStateAsync();
    }

    private async Task StartPresetAsync(SessionPresetViewModel presetVm)
    {
        try
        {
            SessionStatus = $"Starting '{presetVm.Name}'...";

            var result = await _sessionsApi.StartSessionAsync(presetVm.Id);
            if (!result.Success)
                SessionStatus = $"Failed to start session: {result.Message}";
            else
                SessionStatus = $"Session '{presetVm.Name}' is now active.";
        }
        catch (Exception ex)
        {
            SessionStatus = $"Failed to start session: {ex.Message}";
        }
    }

    private async Task StopCurrentSessionAsync()
    {
        try
        {
            SessionStatus = "Stopping session...";

            var result = await _sessionsApi.StopSessionAsync();
            if (!result.Success)
                SessionStatus = $"Failed to stop session: {result.Message}";
            else
                SessionStatus = "Session stopped successfully.";
        }
        catch (Exception ex)
        {
            SessionStatus = $"Failed to stop session: {ex.Message}";
        }
    }

    private void EditPreset(SessionPresetViewModel presetVm)
    {
        var editor = _serviceProvider.GetRequiredService<SessionEditorViewModel>();
        editor.PresetToEdit = presetVm.Model;
        _shell.NavigateTo(editor);
    }

    private void CreateNewSession()
    {
        var editor = _serviceProvider.GetRequiredService<SessionEditorViewModel>();
        editor.PresetToEdit = null;
        _shell.NavigateTo(editor);
    }

    private async Task DeletePresetAsync(SessionPresetViewModel presetVm)
    {
        // Show confirmation dialog
        var window = Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (window == null)
        {
            SessionStatus = "Cannot show confirmation dialog - window not available.";
            return;
        }

        Window? dialog = null;
        dialog = new Window
        {
            Title = "Delete Preset",
            Width = 400,
            Height = 200,
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
                        Text = $"Delete preset '{presetVm.Name}'?",
                        FontSize = 16,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "This action cannot be undone.",
                        Opacity = 0.7,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 10)
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 10,
                        Children =
                        {
                            new Button
                            {
                                Content = "Delete",
                                Width = 100,
                                Command = ReactiveCommand.Create(() => dialog!.Close(true))
                            },
                            new Button
                            {
                                Content = "Cancel",
                                Width = 100,
                                Command = ReactiveCommand.Create(() => dialog!.Close(false))
                            }
                        }
                    }
                }
            }
        };

        var result = await dialog.ShowDialog<bool>(window);

        if (!result) return;

        try
        {
            await _presetsApi.DeletePresetAsync(presetVm.Id);
            Presets.Remove(presetVm);
            presetVm.Dispose();
            SessionStatus = $"Preset '{presetVm.Name}' deleted.";
        }
        catch (Exception ex)
        {
            SessionStatus = $"Failed to delete preset: {ex.Message}";
        }
    }

    private async Task LoadPresetsAsync()
    {
        try
        {
            var presets = await _presetsApi.ListPresetsAsync();

            var modulesApi = _serviceProvider.GetRequiredService<IModulesApi>();
            var modules = await modulesApi.ListModulesAsync();

            // Build VMs off-UI thread
            var newVms = new List<SessionPresetViewModel>();
            foreach (var summary in presets)
            {
                var fullPreset = await _presetsApi.GetPresetAsync(summary.Id);
                if (fullPreset != null) newVms.Add(new SessionPresetViewModel(fullPreset, modules, modulesApi));
            }

            // Apply to ObservableCollection on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var preset in Presets) preset.Dispose();
                Presets.Clear();

                foreach (var vm in newVms) Presets.Add(vm);

                if (Presets.Count == 0) SessionStatus = "No presets found. Click 'Create New Session' to get started.";
            });
        }
        catch (Exception ex)
        {
            SessionStatus = $"Failed to load presets: {ex.Message}";
        }
    }

    private async Task RefreshSessionStateAsync()
    {
        try
        {
            var state = await _sessionsApi.GetCurrentSessionAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (state?.IsActive == true)
                {
                    SessionStatus = state.PresetName != null
                        ? $"Session '{state.PresetName}' is active."
                        : "Session is active.";
                    ActiveSessionPresetId = state.PresetId;
                    IsSessionActive = true;
                }
                else
                {
                    SessionStatus = "No session is active.";
                    ActiveSessionPresetId = null;
                    IsSessionActive = false;
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SessionStatus = $"Unable to fetch session state: {ex.Message}";
                ActiveSessionPresetId = null;
                IsSessionActive = false;
            });
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        foreach (var preset in Presets)
            preset.Dispose();
    }
}