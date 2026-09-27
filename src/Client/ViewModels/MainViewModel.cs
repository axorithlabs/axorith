using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Channels;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services;
using Axorith.Client.Services.Abstractions;
using Axorith.Core.Models;
using Axorith.Telemetry;
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
    private readonly IUpdatesApi _updatesApi;
    private readonly IServiceProvider _serviceProvider;
    private readonly CompositeDisposable _disposables = [];
    private readonly ITelemetryService? _telemetry;
    private readonly IClientOnboardingService? _onboardingService;
    private readonly IToastNotificationService? _toastService;

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

    public string SessionStatus
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsSessionStatusNoticeVisible));
        }
    } = "No session is active.";

    public bool IsSessionStatusNoticeVisible => !IsOverlayOpen &&
        (!IsSessionActive || SessionStatus.StartsWith("Failed", StringComparison.Ordinal) ||
         SessionStatus.StartsWith("Unable", StringComparison.Ordinal)) &&
        !string.IsNullOrWhiteSpace(SessionStatus) && SessionStatus != "No session is active.";

    public bool IsSessionActive
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsCommittedSession));
            this.RaisePropertyChanged(nameof(CanStopSession));
            this.RaisePropertyChanged(nameof(IsSessionStatusNoticeVisible));
        }
    }

    public FocusCommitmentMode ActiveFocusCommitmentMode
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsCommittedSession));
            this.RaisePropertyChanged(nameof(CanStopSession));
        }
    }

    public bool IsCommittedSession => IsSessionActive && ActiveFocusCommitmentMode != FocusCommitmentMode.Normal;
    public bool CanStopSession => IsSessionActive && !IsCommittedSession;

    public string ActiveSessionMode => ActiveFocusCommitmentMode switch
    {
        FocusCommitmentMode.Locked => "Locked",
        FocusCommitmentMode.Strict => "Strict",
        _ => "Normal"
    };

    private string _activeWorkspaceName = string.Empty;
    public string ActiveWorkspaceName { get => _activeWorkspaceName; private set => this.RaiseAndSetIfChanged(ref _activeWorkspaceName, value); }
    private string _activeSessionRemaining = string.Empty;
    public string ActiveSessionRemaining
    {
        get => _activeSessionRemaining;
        private set
        {
            this.RaiseAndSetIfChanged(ref _activeSessionRemaining, value);
        }
    }
    private string _activeSessionEndTime = string.Empty;
    public string ActiveSessionEndTime { get => _activeSessionEndTime; private set => this.RaiseAndSetIfChanged(ref _activeSessionEndTime, value); }
    private string _activeBreakStatus = string.Empty;
    public string ActiveBreakStatus
    {
        get => _activeBreakStatus;
        private set
        {
            this.RaiseAndSetIfChanged(ref _activeBreakStatus, value);
            this.RaisePropertyChanged(nameof(HasActiveBreakStatus));
        }
    }
    public bool HasActiveBreakStatus => !string.IsNullOrWhiteSpace(ActiveBreakStatus);
    private string _activeProtectionStatus = "Protection inactive";
    public string ActiveProtectionStatus
    {
        get => _activeProtectionStatus;
        private set
        {
            this.RaiseAndSetIfChanged(ref _activeProtectionStatus, value);
            this.RaisePropertyChanged(nameof(ActiveProtectionBrush));
            this.RaisePropertyChanged(nameof(IsProtectionAttentionVisible));
        }
    }
    public bool IsProtectionAttentionVisible =>
        !ActiveProtectionStatus.StartsWith("Protection active", StringComparison.Ordinal) &&
        !ActiveProtectionStatus.StartsWith("Protection inactive", StringComparison.Ordinal);
    public IBrush ActiveProtectionBrush => ActiveProtectionStatus switch
    {
        var status when status.StartsWith("Protection failed", StringComparison.Ordinal) => Brushes.IndianRed,
        var status when status.StartsWith("Protection degraded", StringComparison.Ordinal) => Brushes.Orange,
        var status when status.StartsWith("Protection recovered", StringComparison.Ordinal) => Brushes.LightSkyBlue,
        var status when status.StartsWith("Protection active", StringComparison.Ordinal) => Brushes.LightGreen,
        _ => Brushes.Gray
    };
    private bool _canStartBreak;
    public bool CanStartBreak { get => _canStartBreak; private set => this.RaiseAndSetIfChanged(ref _canStartBreak, value); }
    public ICommand StartBreakCommand { get; }

    public bool IsEmergencyUnlockOpen
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsOverlayOpen));
            this.RaisePropertyChanged(nameof(IsSessionStatusNoticeVisible));
        }
    }

    public double EmergencyUnlockProgress
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string EmergencyUnlockError
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasEmergencyUnlockError));
        }
    } = string.Empty;
    public bool HasEmergencyUnlockError => !string.IsNullOrWhiteSpace(EmergencyUnlockError);

    public bool IsStartConfirmationOpen
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsOverlayOpen));
            this.RaisePropertyChanged(nameof(IsSessionStatusNoticeVisible));
        }
    }

    public bool IsOverlayOpen => IsStartConfirmationOpen || IsEmergencyUnlockOpen;

    public bool IsStartCountdownRunning
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsDestructiveStartReviewAfterEnd
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int StartCountdownSeconds
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string StartReviewWorkspaceName
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string StartReviewTitle
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public bool IsStrictStartReview => _pendingStartPreset?.Model.FocusCommitment.Mode == FocusCommitmentMode.Strict;

    public string StartReviewEndTime
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string StartReviewApps
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string StartReviewBlockers
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string StartReviewBreakPolicy
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string StartReviewAfterEnd
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    private CancellationTokenSource? _emergencyUnlockCts;
    private CancellationTokenSource? _startCountdownCts;
    private SessionPresetViewModel? _pendingStartPreset;
    private readonly DispatcherTimer _activeSessionClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? _activeSessionEndsAt;
    private DateTimeOffset? _activeBreakEndsAt;
    private long? _activeSessionRemainingDeadline;
    private long? _activeBreakRemainingDeadline;
    private int _activeBreaksRemaining;
    private DateTimeOffset _lastSessionStatePoll = DateTimeOffset.MinValue;
    private bool _sessionStatePollRunning;

    public bool UpdateAvailable
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? UpdateVersion
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsDownloadingUpdate
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public double DownloadProgress
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    private UpdateInfoDto? _availableUpdate;

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

    public ICommand OpenSettingsCommand { get; }

    public ICommand InstallUpdateCommand { get; }

    public ICommand RunSetupWizardCommand { get; }
    public ICommand OpenEmergencyUnlockCommand { get; }
    public ICommand CloseEmergencyUnlockCommand { get; }
    public ICommand ConfirmStartCommand { get; }
    public ICommand CancelStartCommand { get; }

    public bool IsRunningSetup
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public MainViewModel(ShellViewModel shell, IPresetsApi presetsApi, ISessionsApi sessionsApi,
        IUpdatesApi updatesApi, IServiceProvider serviceProvider)
    {
        _shell = shell;
        _presetsApi = presetsApi;
        _sessionsApi = sessionsApi;
        _updatesApi = updatesApi;
        _serviceProvider = serviceProvider;
        _telemetry = serviceProvider.GetService<ITelemetryService>();
        _onboardingService = serviceProvider.GetService<IClientOnboardingService>();
        _toastService = serviceProvider.GetService<IToastNotificationService>();
        _activeSessionClock.Tick += (_, _) =>
        {
            UpdateActiveSessionClock();
            if (IsCommittedSession && !_sessionStatePollRunning &&
                DateTimeOffset.UtcNow - _lastSessionStatePoll >= TimeSpan.FromSeconds(5))
            {
                _lastSessionStatePoll = DateTimeOffset.UtcNow;
                _ = RefreshSessionStateForClockAsync();
            }
        };
        _activeSessionClock.Start();

        var subscription = _sessionsApi.SessionEvents
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(evt =>
            {
                switch (evt.Type)
                {
                    case SessionEventType.Started:
                        SessionStatus = "Session is active.";
                        ActiveSessionPresetId = evt.PresetId;
                        ActiveFocusCommitmentMode = FocusCommitmentMode.Locked;
                        IsSessionActive = true;
                        _ = RefreshSessionStateAsync();
                        break;
                    case SessionEventType.Stopped:
                        SessionStatus = "No session is active.";
                        ActiveSessionPresetId = null;
                        ActiveFocusCommitmentMode = FocusCommitmentMode.Normal;
                        IsSessionActive = false;
                        IsEmergencyUnlockOpen = false;
                        ResetActiveSessionDetails();
                        break;
                    case SessionEventType.ModuleStarted:
                    case SessionEventType.ModuleStopped:
                    case SessionEventType.ModuleError:
                        _telemetry?.TrackEvent("ModuleUsed", new Dictionary<string, object?>
                        {
                            ["event"] = evt.Type.ToString(),
                            ["module"] = evt.Message
                        });
                        if (evt.Type == SessionEventType.ModuleError)
                        {
                            _telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
                            {
                                ["message"] = evt.Message,
                                ["fatal"] = false
                            });
                        }

                        break;
                    case SessionEventType.ValidationWarning:
                        SessionStatus = evt.Message ?? "Session validation warning.";
                        _telemetry?.TrackEvent("SessionValidationWarning", new Dictionary<string, object?>
                        {
                            ["message"] = evt.Message,
                            ["presetId"] = evt.PresetId?.ToString()
                        });
                        break;
                    default:
                        _telemetry?.TrackEvent("SessionEventUnhandled", new Dictionary<string, object?>
                        {
                            ["event"] = evt.Type.ToString(),
                            ["message"] = evt.Message,
                            ["presetId"] = evt.PresetId?.ToString()
                        });
                        break;
                }
            });

        _disposables.Add(subscription);

        var canManipulatePreset = this
            .WhenAnyValue(vm => vm.IsSessionActive, vm => vm.IsOverlayOpen,
                (isActive, overlayOpen) => !isActive && !overlayOpen)
            .ObserveOn(RxApp.MainThreadScheduler);
        var canStopSession = this
            .WhenAnyValue(vm => vm.IsSessionActive, vm => vm.ActiveFocusCommitmentMode, vm => vm.IsOverlayOpen,
                (active, mode, overlayOpen) => active && mode == FocusCommitmentMode.Normal && !overlayOpen)
            .ObserveOn(RxApp.MainThreadScheduler);

        DeleteSelectedCommand =
            ReactiveCommand.CreateFromTask<SessionPresetViewModel>(DeletePresetAsync, canManipulatePreset);
        EditSelectedCommand = ReactiveCommand.Create<SessionPresetViewModel>(EditPreset, canManipulatePreset);
        StartSelectedCommand =
            ReactiveCommand.CreateFromTask<SessionPresetViewModel>(StartPresetAsync, canManipulatePreset);
        ConfirmStartCommand = ReactiveCommand.CreateFromTask(ConfirmStartAsync);
        CancelStartCommand = ReactiveCommand.Create(CancelStart);
        StopSessionCommand = ReactiveCommand.CreateFromTask(StopCurrentSessionAsync, canStopSession);
        OpenEmergencyUnlockCommand = ReactiveCommand.Create(() =>
        {
            if (IsCommittedSession)
            {
                IsEmergencyUnlockOpen = true;
                EmergencyUnlockError = string.Empty;
            }
        });
        CloseEmergencyUnlockCommand = ReactiveCommand.Create(() =>
        {
            EndEmergencyUnlockHold();
            IsEmergencyUnlockOpen = false;
        });
        LoadPresetsCommand = ReactiveCommand.CreateFromTask(LoadPresetsAsync, canManipulatePreset);
        CreateSessionCommand = ReactiveCommand.Create(CreateNewSession, canManipulatePreset);
        OpenSettingsCommand = ReactiveCommand.Create(OpenSettings,
            this.WhenAnyValue(vm => vm.IsOverlayOpen, overlayOpen => !overlayOpen));

        var canInstallUpdate = this
            .WhenAnyValue(vm => vm.UpdateAvailable, vm => vm.IsDownloadingUpdate, vm => vm.IsCommittedSession,
                (available, downloading, committed) => available && !downloading && !committed)
            .ObserveOn(RxApp.MainThreadScheduler);
        InstallUpdateCommand = ReactiveCommand.CreateFromTask(InstallUpdateAsync, canInstallUpdate);

        StartBreakCommand = ReactiveCommand.CreateFromTask(StartBreakAsync,
            this.WhenAnyValue(vm => vm.CanStartBreak, vm => vm.IsOverlayOpen,
                (canStart, overlayOpen) => canStart && !overlayOpen));

        var canRunSetup = this.WhenAnyValue(vm => vm.IsRunningSetup, running => !running);
        RunSetupWizardCommand = ReactiveCommand.CreateFromTask(RunSetupWizardAsync, canRunSetup);
    }

    /// <summary>
    ///     Asynchronously initializes the ViewModel by loading the initial list of presets.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadPresetsAsync();
        await RefreshSessionStateAsync();
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateInfo = await _updatesApi.GetUpdateInfoAsync();
            if (updateInfo != null)
            {
                _availableUpdate = updateInfo;
                UpdateAvailable = true;
                UpdateVersion = updateInfo.Version;

                _telemetry?.TrackEvent("UpdateAvailable", new Dictionary<string, object?>
                {
                    ["version"] = updateInfo.Version
                });
            }
        }
        catch (Exception)
        {
            _telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
            {
                ["message"] = "Failed to check for updates",
                ["fatal"] = false
            });
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate == null)
        {
            return;
        }

        try
        {
            IsDownloadingUpdate = true;
            DownloadProgress = 0;

            var progress = new Progress<double>(value => { DownloadProgress = value; });

            var installerPath = await _updatesApi.DownloadUpdateAsync(_availableUpdate, progress);

            _telemetry?.TrackEvent("UpdateInstalled", new Dictionary<string, object?>
            {
                ["version"] = _availableUpdate.Version
            });

            await _updatesApi.InstallUpdateAsync(installerPath);
        }
        catch (Exception)
        {
            IsDownloadingUpdate = false;
            _telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
            {
                ["message"] = "Failed to install update",
                ["fatal"] = false
            });
        }
    }

    private async Task StartPresetAsync(SessionPresetViewModel presetVm)
    {
        try
        {
            if (presetVm.Model.FocusCommitment.Mode == FocusCommitmentMode.Normal)
            {
                await LaunchPresetAsync(presetVm);
                return;
            }

            SessionStatus = $"Checking '{presetVm.Name}' before the committed start...";
            var result = await _sessionsApi.PreflightSessionAsync(presetVm.Id);
            if (!result.Success)
            {
                var error = $"{presetVm.Model.FocusCommitment.Mode} Session cannot start: {result.Message}";
                SessionStatus = error;
                ShowTransientSessionError(error, TimeSpan.FromSeconds(8));
                return;
            }

            _pendingStartPreset = presetVm;
            PopulateStartReview(presetVm);
            StartCountdownSeconds = 0;
            IsStartCountdownRunning = false;
            IsStartConfirmationOpen = true;
            SessionStatus = $"Review '{presetVm.Name}' before starting.";
        }
        catch (Exception ex)
        {
            var error = $"Session preflight failed: {ex.Message}";
            SessionStatus = error;
            ShowTransientSessionError(error, TimeSpan.FromSeconds(5));
        }
    }

    private void PopulateStartReview(SessionPresetViewModel presetVm)
    {
        var options = presetVm.Model.FocusCommitment;
        StartReviewWorkspaceName = presetVm.Name;
        StartReviewTitle = $"Start {options.Mode} Session?";
        this.RaisePropertyChanged(nameof(IsStrictStartReview));
        StartReviewEndTime = options.EndCondition switch
        {
            FocusEndCondition.Duration => $"Duration: {options.Duration?.TotalMinutes:0} minutes · ends at {DateTimeOffset.Now.Add(options.Duration ?? TimeSpan.FromHours(1)).AddSeconds(10):HH:mm}",
            FocusEndCondition.EndAt => FormatEndAtReview(options, DateTime.Now),
            _ => "No fixed end time"
        };

        var apps = presetVm.Modules
            .Where(module => module.Definition.Name == "Application Launcher")
            .Select(module =>
            {
                var path = module.Model.Settings.GetValueOrDefault("ApplicationPath");
                if (string.Equals(path, "custom-app", StringComparison.OrdinalIgnoreCase))
                    path = module.Model.Settings.GetValueOrDefault("CustomPath");
                var executableName = Path.GetFileNameWithoutExtension(path ?? string.Empty);
                return string.IsNullOrWhiteSpace(executableName) ? module.DisplayName : executableName;
            })
            .ToArray();
        StartReviewApps = apps.Length == 0 ? "No applications will be opened." : string.Join(", ", apps);

        var blockers = new List<string>();
        foreach (var module in presetVm.Modules.Where(module => module.Definition.Name is "App Blocker" or "Site Blocker"))
        {
            if (module.Definition.Name == "App Blocker" &&
                module.Model.Settings.GetValueOrDefault("Mode") == "AllowList")
            {
                var allowedApps = presetVm.Modules
                    .Where(app => app.Definition.Name == "Application Launcher")
                    .Select(app =>
                    {
                        var path = app.Model.Settings.GetValueOrDefault("ApplicationPath");
                        if (string.Equals(path, "custom-app", StringComparison.OrdinalIgnoreCase))
                            path = app.Model.Settings.GetValueOrDefault("CustomPath");
                        return Path.GetFileNameWithoutExtension(path ?? string.Empty);
                    })
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                blockers.Add($"Only allow Workspace apps: {string.Join(", ", allowedApps)}");
            }
            else
            {
                blockers.Add(module.Definition.Name);
            }
        }

        var blockerList = blockers.Distinct().ToArray();
        StartReviewBlockers = blockerList.Length == 0 ? "No blockers configured." : string.Join(" · ", blockerList);

        StartReviewBreakPolicy = options.BreakCount == 0
            ? "No breaks"
            : $"{options.BreakCount} break{(options.BreakCount == 1 ? string.Empty : "s")} · {options.BreakDuration.TotalMinutes:0} min each";

        StartReviewAfterEnd = options.AfterEnd switch
        {
            AfterEndBehavior.StartNextWorkspace => $"Start next Workspace: {Presets.FirstOrDefault(p => p.Id == options.NextWorkspaceId)?.Name ?? "not selected"}",
            AfterEndBehavior.LockPc => "Lock PC",
            AfterEndBehavior.Sleep => "Sleep",
            AfterEndBehavior.SignOut => "Sign out",
            AfterEndBehavior.ShutDownPc => "Shut down PC",
            _ => "Do nothing"
        };
        IsDestructiveStartReviewAfterEnd = options.AfterEnd is AfterEndBehavior.SignOut or AfterEndBehavior.ShutDownPc;
    }

    private static string FormatEndAtReview(FocusCommitmentOptions options, DateTime localNow)
    {
        if (options.EndAtLocalTime is not { } endTime)
        {
            return "End time is not set.";
        }

        for (var daysAhead = 0; daysAhead <= 7; daysAhead++)
        {
            var date = localNow.Date.AddDays(daysAhead);
            if (options.EndAtDaysOfWeek is { Count: > 0 } days && !days.Contains(date.DayOfWeek))
            {
                continue;
            }

            var end = date.Add(endTime.ToTimeSpan());
            if (end > localNow)
            {
                return $"Ends {end:ddd, MMM d} at {end:HH:mm} local time";
            }
        }

        return "No valid end day is selected.";
    }

    private async Task ConfirmStartAsync()
    {
        if (_pendingStartPreset == null || IsStartCountdownRunning)
        {
            return;
        }

        var preset = _pendingStartPreset;
        var cts = new CancellationTokenSource();
        _startCountdownCts = cts;
        IsStartCountdownRunning = true;

        try
        {
            for (var seconds = 10; seconds > 0; seconds--)
            {
                StartCountdownSeconds = seconds;
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            StartCountdownSeconds = 0;
            SessionStatus = "Checking protection again before the committed start...";
            var preflight = await _sessionsApi.PreflightSessionAsync(preset.Id, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!preflight.Success)
            {
                var error = $"{preset.Model.FocusCommitment.Mode} Session cannot start: {preflight.Message}";
                _pendingStartPreset = null;
                IsStartConfirmationOpen = false;
                SessionStatus = error;
                ShowTransientSessionError(error, TimeSpan.FromSeconds(8));
                return;
            }

            _pendingStartPreset = null;
            IsStartConfirmationOpen = false;
            IsStartCountdownRunning = false;
            await LaunchPresetAsync(preset);
        }
        catch (OperationCanceledException)
        {
            // The user cancelled during the ten-second countdown.
        }
        finally
        {
            IsStartCountdownRunning = false;
            StartCountdownSeconds = 0;
            if (ReferenceEquals(_startCountdownCts, cts))
            {
                _startCountdownCts = null;
            }
            cts.Dispose();
        }
    }

    private void CancelStart()
    {
        _startCountdownCts?.Cancel();
        _startCountdownCts = null;
        _pendingStartPreset = null;
        IsStartCountdownRunning = false;
        StartCountdownSeconds = 0;
        IsStartConfirmationOpen = false;
        SessionStatus = "Committed session start cancelled.";
    }

    private async Task LaunchPresetAsync(SessionPresetViewModel presetVm)
    {
        try
        {
            SessionStatus = $"Starting '{presetVm.Name}'...";
            ActiveSessionPresetId = presetVm.Id;
            ActiveFocusCommitmentMode = presetVm.Model.FocusCommitment.Mode;
            IsSessionActive = true;

            var result = await _sessionsApi.StartSessionAsync(presetVm.Id);
            if (!result.Success)
            {
                var error = $"Failed to start session: {result.Message}";
                SessionStatus = error;
                ActiveSessionPresetId = null;
                ActiveFocusCommitmentMode = FocusCommitmentMode.Normal;
                IsSessionActive = false;
                ShowTransientSessionError(error, TimeSpan.FromSeconds(5));
            }
            else
            {
                SessionStatus = $"Session '{presetVm.Name}' is now active.";
                await RefreshSessionStateAsync();
            }
        }
        catch (Exception ex)
        {
            var error = $"Failed to start session: {ex.Message}";
            SessionStatus = error;
            ActiveSessionPresetId = null;
            ActiveFocusCommitmentMode = FocusCommitmentMode.Normal;
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
                SessionStatus = "Session stopped.";
                ActiveSessionPresetId = null;
                ActiveFocusCommitmentMode = FocusCommitmentMode.Normal;
                IsSessionActive = false;
            }
        }
        catch (Exception ex)
        {
            SessionStatus = $"Failed to stop session: {ex.Message}";
        }
    }

    public void BeginEmergencyUnlockHold()
    {
        if (!IsCommittedSession || _emergencyUnlockCts != null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        _emergencyUnlockCts = cts;
        EmergencyUnlockProgress = 0;
        EmergencyUnlockError = string.Empty;
        _ = RunEmergencyUnlockHoldAsync(channel, cts);
    }

    public void EndEmergencyUnlockHold()
    {
        var cts = _emergencyUnlockCts;
        _emergencyUnlockCts = null;
        cts?.Cancel();

        if (IsCommittedSession)
        {
            EmergencyUnlockProgress = 0;
        }
    }

    private async Task RunEmergencyUnlockHoldAsync(Channel<bool> signals, CancellationTokenSource cts)
    {
        var token = cts.Token;
        var completed = false;
        var producer = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
                do
                {
                    await signals.Writer.WriteAsync(true, token).ConfigureAwait(false);
                } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                // Releasing the input cancels the Host-controlled hold stream.
            }
        }, token);

        try
        {
            await foreach (var progress in _sessionsApi.HoldEmergencyUnlockAsync(
                               signals.Reader.ReadAllAsync(token), token).ConfigureAwait(false))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    EmergencyUnlockProgress = progress.Progress;
                    if (!progress.Completed && progress.Progress == 0 &&
                        progress.Message != "Keep holding to unlock.")
                    {
                        EmergencyUnlockError = progress.Message;
                    }
                });

                if (!progress.Completed)
                {
                    continue;
                }

                completed = true;
                break;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the user releases the hold or leaves the window.
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => EmergencyUnlockError =
                $"Emergency Unlock connection failed: {ex.Message}");
        }
        finally
        {
            signals.Writer.TryComplete();
            cts.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the hold ends.
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_emergencyUnlockCts, cts))
                {
                    _emergencyUnlockCts = null;
                }

                if (completed)
                {
                    EmergencyUnlockProgress = 1;
                    IsEmergencyUnlockOpen = false;
                }
                else if (IsCommittedSession && EmergencyUnlockProgress > 0)
                {
                    EmergencyUnlockProgress = 0;
                }
            });

            cts.Dispose();
            if (completed)
            {
                await RefreshSessionStateAsync();
                await Dispatcher.UIThread.InvokeAsync(() => SessionStatus = "Emergency Unlock completed.");
            }
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

    private void OpenSettings()
    {
        var settings = _serviceProvider.GetRequiredService<SettingsViewModel>();
        _shell.NavigateTo(settings);
    }

    private async Task RunSetupWizardAsync()
    {
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
            var fullPresets = new List<SessionPreset>();
            foreach (var summary in presets)
            {
                var fullPreset = await _presetsApi.GetPresetAsync(summary.Id);
                if (fullPreset != null)
                {
                    fullPresets.Add(fullPreset);
                    newVms.Add(new SessionPresetViewModel(fullPreset, modules, modulesApi, _serviceProvider));
                }
            }

            var moduleDefLookup = modules.ToDictionary(m => m.Id, m => m.Name);

            var presetData = fullPresets.Select(p => new Dictionary<string, object?>
            {
                ["id"] = p.Id.ToString(),
                ["version"] = p.Version,
                ["moduleCount"] = p.Modules.Count,
                ["modules"] = p.Modules.Select(m =>
                {
                    var moduleName = moduleDefLookup.TryGetValue(m.ModuleId, out var name) ? name : "Unknown";
                    return new Dictionary<string, object?>
                    {
                        ["instanceId"] = m.InstanceId.ToString(),
                        ["moduleId"] = m.ModuleId.ToString(),
                        ["moduleName"] = TelemetryGuard.SafeString(moduleName),
                        ["startDelayMs"] = (long)m.StartDelay.TotalMilliseconds,
                        ["settingsCount"] = m.Settings.Count
                    };
                }).ToArray()
            }).ToArray();

            _telemetry?.TrackEvent("PresetCount", new Dictionary<string, object?>
            {
                ["total"] = presets.Count,
                ["presets"] = presetData
            });

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
                    ActiveFocusCommitmentMode = state.FocusCommitment;
                    IsSessionActive = true;
                    ActiveWorkspaceName = state.PresetName ?? "Active Workspace";
                    _activeSessionEndsAt = state.EndsAt;
                    _activeBreakEndsAt = state.BreakEndsAt;
                    _activeSessionRemainingDeadline = state.Remaining is { } sessionRemaining
                        ? GetStopwatchDeadline(sessionRemaining)
                        : null;
                    _activeBreakRemainingDeadline = state.BreakRemaining is { } breakRemaining
                        ? GetStopwatchDeadline(breakRemaining)
                        : null;
                    _activeBreaksRemaining = state.BreaksRemaining;
                    ActiveProtectionStatus = ToProtectionLabel(state.ProtectionStatus);
                    this.RaisePropertyChanged(nameof(ActiveSessionMode));
                    UpdateCanStartBreak();
                    UpdateActiveSessionClock();
                }
                else
                {
                    SessionStatus = "No session is active.";
                    ActiveSessionPresetId = null;
                    ActiveFocusCommitmentMode = FocusCommitmentMode.Normal;
                    IsSessionActive = false;
                    IsEmergencyUnlockOpen = false;
                    ResetActiveSessionDetails();
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SessionStatus = $"Unable to fetch session state: {ex.Message}";
                ActiveSessionPresetId = null;
                ActiveFocusCommitmentMode = FocusCommitmentMode.Normal;
                IsSessionActive = false;
                ResetActiveSessionDetails();
            });
        }
    }

    public void Dispose()
    {
        EndEmergencyUnlockHold();
        _activeSessionClock.Stop();
        _disposables.Dispose();
        foreach (var preset in Presets)
        {
            preset.Dispose();
        }
    }

    private async Task StartBreakAsync()
    {
        var result = await _sessionsApi.StartBreakAsync();
        if (!result.Success)
        {
            SessionStatus = result.Message;
            return;
        }

        await RefreshSessionStateAsync();
    }

    private async Task RefreshSessionStateForClockAsync()
    {
        _sessionStatePollRunning = true;
        try
        {
            await RefreshSessionStateAsync();
        }
        finally
        {
            _sessionStatePollRunning = false;
        }
    }

    private void UpdateCanStartBreak()
    {
        CanStartBreak = IsCommittedSession && !_activeBreakEndsAt.HasValue && _activeBreaksRemaining > 0;
    }

    private void UpdateActiveSessionClock()
    {
        if (!IsSessionActive || !_activeSessionEndsAt.HasValue)
        {
            ActiveSessionRemaining = string.Empty;
            ActiveSessionEndTime = string.Empty;
        }
        else
        {
            var remaining = _activeSessionRemainingDeadline is { } deadline
                ? GetRemainingFromStopwatch(deadline)
                : _activeSessionEndsAt.Value - DateTimeOffset.UtcNow;
            var minutes = Math.Max(0, (int)Math.Ceiling(remaining.TotalMinutes));
            ActiveSessionRemaining = minutes == 0 ? "Ending…" : $"{minutes} min remaining";
            ActiveSessionEndTime = $"Ends at {_activeSessionEndsAt.Value.ToLocalTime():HH:mm}";
        }

        if (_activeBreakEndsAt is { } breakEndsAt)
        {
            var remaining = _activeBreakRemainingDeadline is { } deadline
                ? GetRemainingFromStopwatch(deadline)
                : breakEndsAt - DateTimeOffset.UtcNow;
            var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            ActiveBreakStatus = $"Break in progress · {seconds / 60:00}:{seconds % 60:00} remaining";
        }
        else
        {
            ActiveBreakStatus = string.Empty;
        }

    }

    private void ResetActiveSessionDetails()
    {
        _activeSessionEndsAt = null;
        ActiveWorkspaceName = string.Empty;
        _activeBreakEndsAt = null;
        _activeSessionRemainingDeadline = null;
        _activeBreakRemainingDeadline = null;
        _activeBreaksRemaining = 0;
        ActiveBreakStatus = string.Empty;
        ActiveProtectionStatus = "Protection inactive";
        UpdateCanStartBreak();
        UpdateActiveSessionClock();
    }

    private static string ToProtectionLabel(string status) =>
        status.StartsWith("Protection ", StringComparison.Ordinal) ? status : "Protection inactive";

    private static long GetStopwatchDeadline(TimeSpan remaining)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = Math.Max(0, remaining.TotalSeconds);
        var available = long.MaxValue - now;
        return seconds >= (double)available / System.Diagnostics.Stopwatch.Frequency
            ? long.MaxValue
            : now + (long)(seconds * System.Diagnostics.Stopwatch.Frequency);
    }

    private static TimeSpan GetRemainingFromStopwatch(long deadline)
    {
        var ticks = Math.Max(0, deadline - System.Diagnostics.Stopwatch.GetTimestamp());
        return TimeSpan.FromSeconds((double)ticks / System.Diagnostics.Stopwatch.Frequency);
    }
}
