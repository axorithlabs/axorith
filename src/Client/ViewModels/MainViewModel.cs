using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using Axorith.Client.CoreSdk.Abstractions;
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
    private readonly CompositeDisposable _disposables = [];

    /// <summary>
    ///     The currently selected session preset in the list.
    /// </summary>
    public SessionPresetViewModel? SelectedPreset
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public Guid? ActiveSessionPresetId
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    ///     A user-friendly string describing the current session status.
    /// </summary>
    public string SessionStatus
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "No session is active.";

    /// <summary>
    ///     Gets a value indicating whether a session is currently active.
    ///     This property is exposed for binding in the View.
    /// </summary>
    public bool IsSessionActive
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
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
            ActiveSessionPresetId = presetVm.Id;
            IsSessionActive = true;

            var result = await _sessionsApi.StartSessionAsync(presetVm.Id);
            if (!result.Success)
            {
                var error = $"Failed to start session: {result.Message}";
                SessionStatus = error;
                ActiveSessionPresetId = null;
                IsSessionActive = false;
                ShowTransientSessionError(error, TimeSpan.FromSeconds(5));
            }
            else
            {
                SessionStatus = $"Session '{presetVm.Name}' is now active.";
            }
        }
        catch (Exception ex)
        {
            var error = $"Failed to start session: {ex.Message}";
            SessionStatus = error;
            ActiveSessionPresetId = null;
            IsSessionActive = false;
            ShowTransientSessionError(error, TimeSpan.FromSeconds(5));
        }
    }

    private void ShowTransientSessionError(string message, TimeSpan duration)
    {
        var subscription = Observable
            .Timer(duration)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (!IsSessionActive && SessionStatus == message)
                {
                    SessionStatus = "No session is active.";
                }
            });

        _disposables.Add(subscription);
    }

    private async Task StopCurrentSessionAsync()
    {
        try
        {
            SessionStatus = "Stopping session...";

            var result = await _sessionsApi.StopSessionAsync();
            if (!result.Success)
            {
                SessionStatus = $"Failed to stop session: {result.Message}";
            }
            else
            {
                SessionStatus = "Session stopped successfully.";
                ActiveSessionPresetId = null;
                IsSessionActive = false;
            }
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

            var newVms = new List<SessionPresetViewModel>();
            foreach (var summary in presets)
            {
                var fullPreset = await _presetsApi.GetPresetAsync(summary.Id);
                if (fullPreset != null)
                {
                    newVms.Add(new SessionPresetViewModel(fullPreset, modules, modulesApi, _serviceProvider));
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var preset in Presets)
                {
                    preset.Dispose();
                }

                Presets.Clear();

                foreach (var vm in newVms)
                {
                    Presets.Add(vm);
                }

                if (Presets.Count == 0)
                {
                    SessionStatus = "No presets found. Click 'Create New Session' to get started.";
                }
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
        {
            preset.Dispose();
        }
    }
}