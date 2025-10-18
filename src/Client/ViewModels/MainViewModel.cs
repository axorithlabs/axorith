using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Axorith.Client.ViewModels;

/// <summary>
/// ViewModel for the main dashboard view. Manages the list of presets and session lifecycle commands.
/// </summary>
public class MainViewModel : ReactiveObject
{
    private readonly ShellViewModel _shell;
    private readonly IPresetManager _presetManager;
    private readonly ISessionManager _sessionManager;
    private readonly IServiceProvider _serviceProvider;

    private SessionPreset? _selectedPreset;
    
    /// <summary>
    /// The currently selected session preset in the list.
    /// </summary>
    public SessionPreset? SelectedPreset
    {
        get => _selectedPreset;
        set => this.RaiseAndSetIfChanged(ref _selectedPreset, value);
    }
    
    private string _sessionStatus = "No session is active.";
    
    /// <summary>
    /// A user-friendly string describing the current session status.
    /// </summary>
    public string SessionStatus
    {
        get => _sessionStatus;
        set => this.RaiseAndSetIfChanged(ref _sessionStatus, value);
    }

    /// <summary>
    /// Gets a value indicating whether a session is currently active.
    /// This property is exposed for binding in the View.
    /// </summary>
    public bool IsSessionActive => _sessionManager.IsSessionRunning;

    /// <summary>
    /// A collection of all available session presets loaded from the core.
    /// </summary>
    public ObservableCollection<SessionPreset> Presets { get; } = new();
    
    public ICommand DeleteSelectedCommand { get; }
    public ICommand EditSelectedCommand { get; }
    public ICommand StartSelectedCommand { get; }
    public ICommand StopSessionCommand { get; }
    public ICommand LoadPresetsCommand { get; }
    public ICommand CreateSessionCommand { get; }

    public MainViewModel(ShellViewModel shell, IPresetManager presetManager, ISessionManager sessionManager, IServiceProvider serviceProvider)
    {
        _shell = shell;
        _presetManager = presetManager;
        _sessionManager = sessionManager;
        _serviceProvider = serviceProvider;

        _sessionManager.SessionStarted += OnSessionStarted;
        _sessionManager.SessionStopped += OnSessionStopped;
        
        var canManipulatePreset = this.WhenAnyValue(
            vm => vm.SelectedPreset, 
            vm => vm.IsSessionActive,
            (selected, isActive) => selected != null && !isActive);

        var canStopSession = this.WhenAnyValue(vm => vm.IsSessionActive);
        
        var canDoGlobalActions = this.WhenAnyValue(vm => vm.IsSessionActive, isActive => !isActive);

        DeleteSelectedCommand = ReactiveCommand.CreateFromTask(DeleteSelectedAsync, canManipulatePreset);
        EditSelectedCommand = ReactiveCommand.Create(EditSelected, canManipulatePreset);
        StartSelectedCommand = ReactiveCommand.CreateFromTask(StartSelectedAsync, canManipulatePreset);
        StopSessionCommand = ReactiveCommand.CreateFromTask(_sessionManager.StopCurrentSessionAsync, canStopSession);
        LoadPresetsCommand = ReactiveCommand.CreateFromTask(LoadPresetsAsync, canDoGlobalActions);
        CreateSessionCommand = ReactiveCommand.Create(CreateNewSession, canDoGlobalActions);
    }
    
    private void OnSessionStarted()
    {
        SessionStatus = "Session is active.";
        this.RaisePropertyChanged(nameof(IsSessionActive));
    }

    private void OnSessionStopped()
    {
        SessionStatus = "No session is active.";
        this.RaisePropertyChanged(nameof(IsSessionActive));
    }

    public async Task InitializeAsync()
    {
        await LoadPresetsAsync();
    }

    private async Task StartSelectedAsync()
    {
        if (SelectedPreset is null) return;
        
        try
        {
            await _sessionManager.StartSessionAsync(SelectedPreset);
        }
        catch (SessionException ex)
        {
            SessionStatus = $"Error: {ex.Message}";
        }
    }

    private void EditSelected()
    {
        if (SelectedPreset is null) return;
        
        var editor = _serviceProvider.GetRequiredService<SessionEditorViewModel>();
        editor.PresetToEdit = SelectedPreset;
        _shell.NavigateTo(editor);
    }

    private void CreateNewSession()
    {
        var editor = _serviceProvider.GetRequiredService<SessionEditorViewModel>();
        editor.PresetToEdit = null; 
        _shell.NavigateTo(editor);
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedPreset is null) return;
        
        await _presetManager.DeletePresetAsync(SelectedPreset.Id, CancellationToken.None);
        Presets.Remove(SelectedPreset);
    }

    private async Task LoadPresetsAsync()
    {
        var presetsFromCore = await _presetManager.LoadAllPresetsAsync(CancellationToken.None);
        
        Presets.Clear();
        foreach (var preset in presetsFromCore)
        {
            Presets.Add(preset);
        }
    }
}