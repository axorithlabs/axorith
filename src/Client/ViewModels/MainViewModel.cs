using System.Collections.ObjectModel;
using System.Windows.Input;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace Axorith.Client.ViewModels;

/// <summary>
///     ViewModel for the main dashboard view. Manages the list of presets and session lifecycle commands.
/// </summary>
public class MainViewModel : ReactiveObject
{
    private readonly ShellViewModel _shell;
    private readonly IPresetManager _presetManager;
    private readonly ISessionManager _sessionManager;
    private readonly IServiceProvider _serviceProvider;

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

    /// <summary>
    ///     Gets a value indicating whether a session is currently active.
    ///     This property is exposed for binding in the View.
    /// </summary>
    public bool IsSessionActive => _sessionManager.IsSessionRunning;

    /// <summary>
    ///     A collection of all available session presets loaded from the core.
    /// </summary>
    public ObservableCollection<SessionPresetViewModel> Presets { get; } = new();

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

    public MainViewModel(ShellViewModel shell, IPresetManager presetManager, ISessionManager sessionManager,
        IServiceProvider serviceProvider)
    {
        _shell = shell;
        _presetManager = presetManager;
        _sessionManager = sessionManager;
        _serviceProvider = serviceProvider;

        _sessionManager.SessionStarted += OnSessionStarted;
        _sessionManager.SessionStopped += OnSessionStopped;

                var canManipulatePreset = this.WhenAnyValue(vm => vm.IsSessionActive, isActive => !isActive);
        var canStopSession = this.WhenAnyValue(vm => vm.IsSessionActive);
        var canDoGlobalActions = this.WhenAnyValue(vm => vm.IsSessionActive, isActive => !isActive);

        // These commands now accept a parameter and are always executable when a session is not active.
        DeleteSelectedCommand = ReactiveCommand.CreateFromTask<SessionPresetViewModel>(DeletePresetAsync, canManipulatePreset);
        EditSelectedCommand = ReactiveCommand.Create<SessionPresetViewModel>(EditPreset, canManipulatePreset);
        StartSelectedCommand = ReactiveCommand.CreateFromTask<SessionPresetViewModel>(StartPresetAsync, canManipulatePreset);
        StopSessionCommand = ReactiveCommand.CreateFromTask(_sessionManager.StopCurrentSessionAsync, canStopSession);
        LoadPresetsCommand = ReactiveCommand.CreateFromTask(LoadPresetsAsync, canDoGlobalActions);
        CreateSessionCommand = ReactiveCommand.Create(CreateNewSession, canDoGlobalActions);
    }

        private void OnSessionStarted(Guid presetId)
    {
        SessionStatus = "Session is active.";
        ActiveSessionPresetId = presetId;
        this.RaisePropertyChanged(nameof(IsSessionActive));
    }

        private void OnSessionStopped(Guid presetId)
    {
        SessionStatus = "No session is active.";
        ActiveSessionPresetId = null;
        this.RaisePropertyChanged(nameof(IsSessionActive));
    }

    /// <summary>
    ///     Asynchronously initializes the ViewModel by loading the initial list of presets.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadPresetsAsync();
    }

            private async Task StartPresetAsync(SessionPresetViewModel presetVm)
    {
        if (presetVm is null) return;
        try
        {
            await _sessionManager.StartSessionAsync(presetVm.Model);
        }
        catch (SessionException ex)
        {
            SessionStatus = $"Error: {ex.Message}";
        }
    }

            private void EditPreset(SessionPresetViewModel presetVm)
    {
        if (presetVm is null) return;
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
        if (presetVm is null) return;
        await _presetManager.DeletePresetAsync(presetVm.Id, CancellationToken.None);
        Presets.Remove(presetVm);
    }

        private async Task LoadPresetsAsync()
    {
        var presetsFromCore = await _presetManager.LoadAllPresetsAsync(CancellationToken.None);
        Presets.Clear();
        var moduleRegistry = _serviceProvider.GetRequiredService<IModuleRegistry>();
        foreach (var preset in presetsFromCore)
        {
            Presets.Add(new SessionPresetViewModel(preset, moduleRegistry));
        }
    }
}