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
using Axorith.Sdk.Services;
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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> TrackedUpdateVersions =
        new(StringComparer.OrdinalIgnoreCase);

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

    private bool _sessionStateFailureNotified;

    public bool IsSessionActive
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsCommittedSession));
            this.RaisePropertyChanged(nameof(CanStopSession));
            this.RaisePropertyChanged(nameof(HasActiveSessionName));
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
    public string ActiveWorkspaceName
    {
        get => _activeWorkspaceName;
        private set
        {
            this.RaiseAndSetIfChanged(ref _activeWorkspaceName, value);
            this.RaisePropertyChanged(nameof(HasActiveSessionName));
        }
    }
    public bool HasActiveSessionName => IsSessionActive && !string.IsNullOrWhiteSpace(ActiveWorkspaceName);
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
        ActiveProtectionStatus.StartsWith("Protection failed", StringComparison.Ordinal) ||
        ActiveProtectionStatus.StartsWith("Protection degraded", StringComparison.Ordinal);
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
    private Guid? _pendingStartSessionInstanceId;
    private readonly DispatcherTimer _activeSessionClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? _activeSessionEndsAt;
    private DateTimeOffset? _activeBreakEndsAt;
    private long? _activeSessionRemainingDeadline;
    private long? _activeBreakRemainingDeadline;
    private int _activeBreaksRemaining;
    private DateTimeOffset _lastSessionStatePoll = DateTimeOffset.MinValue;
    private bool _sessionStatePollRunning;
    // ponytail: focus history stays in memory until the host exposes persisted session history.
    private readonly List<SessionActivity> _sessionActivity = [];
    private DateTimeOffset? _trackedSessionStartedAt;
    private Guid? _trackedSessionPresetId;
    private string _trackedSessionPresetName = string.Empty;
    private bool _isPresetsPage;
    private DateTimeOffset _lastDashboardActivityRefresh = DateTimeOffset.MinValue;

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

    public bool IsHomePage => !_isPresetsPage;
    public bool IsPresetsPage => _isPresetsPage;
    public ObservableCollection<string> RecentSessions { get; } = [];
    public bool HasRecentSessions => RecentSessions.Count > 0;

    public string FocusTodayLabel
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "0 min";

    public int SessionsThisWeek
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string FocusThisWeekLabel
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "0 min";

    public double[] FocusActivityValues
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = new double[7];

    public bool HasSessionActivity => _sessionActivity.Count > 0 || _trackedSessionStartedAt.HasValue;

    public string NextScheduledPresetName
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string NextScheduledTime
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string UpcomingScheduleStatus
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "No scheduled preset.";

    public bool HasUpcomingSchedule => !string.IsNullOrWhiteSpace(NextScheduledPresetName);

    public string ActiveBreakPolicy
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "No breaks available";

    public string ActiveAfterEndBehavior
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

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

    public ICommand ShowHomeCommand { get; }

    public ICommand ShowPresetsCommand { get; }

    public ICommand CheckForUpdatesCommand { get; }

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
            UpdateDashboardActivity();
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
                        SetActiveSessionPreset(evt.PresetId);
                        if (Presets.FirstOrDefault(preset => preset.Id == evt.PresetId) is { } activePreset)
                        {
                            ActiveFocusCommitmentMode = activePreset.Model.FocusCommitment.Mode;
                        }
                        IsSessionActive = true;
                        BeginTrackingSession(evt.PresetId, evt.Timestamp);
                        _ = RefreshSessionStateAsync();
                        break;
                    case SessionEventType.Stopped:
                        EndTrackingSession(evt.Timestamp);
                        SetActiveSessionPreset(null);
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
                            _toastService?.Show(evt.Message ?? "A module failed.", NotificationType.Error);
                            _telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
                            {
                                ["message"] = evt.Message,
                                ["fatal"] = false
                            });
                        }

                        break;
                    case SessionEventType.ValidationWarning:
                        _toastService?.Show(evt.Message ?? "Session validation warning.", NotificationType.Warning);
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
        ShowHomeCommand = ReactiveCommand.Create(ShowHome);
        ShowPresetsCommand = ReactiveCommand.Create(ShowPresets);
        CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdatesAsync);

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

    public void ShowHome()
    {
        SetPage(false);
    }

    public void ShowPresets()
    {
        SetPage(true);
    }

    private void SetPage(bool showPresets)
    {
        if (_isPresetsPage == showPresets)
        {
            return;
        }

        _isPresetsPage = showPresets;
        this.RaisePropertyChanged(nameof(IsHomePage));
        this.RaisePropertyChanged(nameof(IsPresetsPage));
    }

    private void SetActiveSessionPreset(Guid? presetId)
    {
        ActiveSessionPresetId = presetId;
        ActiveWorkspaceName = presetId is { } id
            ? Presets.FirstOrDefault(preset => preset.Id == id)?.Name ?? string.Empty
            : string.Empty;
        foreach (var preset in Presets)
        {
            preset.IsActive = preset.Id == presetId;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateInfo = await _updatesApi.GetUpdateInfoAsync();
            _availableUpdate = updateInfo;
            UpdateAvailable = updateInfo != null;
            UpdateVersion = updateInfo?.Version;
            if (updateInfo is not null && _telemetry is { IsEnabled: true } &&
                TryTrackUpdateAvailable(updateInfo.Version))
            {
                _telemetry?.TrackEvent("UpdateAvailable", new Dictionary<string, object?>
                {
                    ["releaseVersion"] = updateInfo.Version
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

    internal static bool TryTrackUpdateAvailable(string releaseVersion) =>
        TrackedUpdateVersions.TryAdd(releaseVersion, 0);

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
        var sessionInstanceId = Guid.NewGuid();
        TrackSessionStartRequested(presetVm, sessionInstanceId);
        try
        {
            if (presetVm.Model.FocusCommitment.Mode == FocusCommitmentMode.Normal)
            {
                await LaunchPresetAsync(presetVm, sessionInstanceId);
                return;
            }

            var result = await _sessionsApi.PreflightSessionAsync(presetVm.Id);
            if (!result.Success)
            {
                TrackSessionStartFailed(presetVm, sessionInstanceId, "preflight", "validation_failed");
                var error = $"{presetVm.Model.FocusCommitment.Mode} Session cannot start: {result.Message}";
                _toastService?.Show(error, NotificationType.Error);
                return;
            }

            _pendingStartPreset = presetVm;
            _pendingStartSessionInstanceId = sessionInstanceId;
            PopulateStartReview(presetVm);
            StartCountdownSeconds = 0;
            IsStartCountdownRunning = false;
            IsStartConfirmationOpen = true;
        }
        catch (Exception ex)
        {
            TrackSessionStartFailed(presetVm, sessionInstanceId, "preflight", "unknown");
            _telemetry?.TrackError(ex, "session", "session_start", "error", handled: true, fatal: false,
                properties: new Dictionary<string, object?>
                {
                    ["sessionInstanceId"] = sessionInstanceId,
                    ["presetId"] = presetVm.Id,
                    ["stage"] = "preflight"
                });
            var error = $"Session preflight failed: {ex.Message}";
            _toastService?.Show(error, NotificationType.Error);
        }
    }

    private void TrackSessionStartRequested(SessionPresetViewModel preset, Guid sessionInstanceId)
    {
        var options = preset.Model.FocusCommitment;
        var modules = preset.Modules.Select(module => new Dictionary<string, object?>
        {
            ["moduleId"] = module.Model.ModuleId,
            ["moduleName"] = module.Definition.Name,
            ["instanceId"] = module.Model.InstanceId
        }).ToArray();

        _telemetry?.TrackEvent("SessionStartRequested", new Dictionary<string, object?>
        {
            ["sessionInstanceId"] = sessionInstanceId,
            ["presetId"] = preset.Id,
            ["startSource"] = "manual",
            ["commitmentMode"] = CommitmentMode(options.Mode),
            ["endConditionType"] = EndCondition(options.EndCondition),
            ["plannedDurationMs"] = options.Duration is { } duration ? (long)duration.TotalMilliseconds : null,
            ["breakCount"] = options.BreakCount,
            ["breakDurationMs"] = (long)options.BreakDuration.TotalMilliseconds,
            ["afterEndAction"] = AfterEndAction(options.AfterEnd),
            ["moduleCount"] = modules.Length,
            ["modules"] = modules,
            ["moduleIds"] = modules.Select(module => ((Guid)module["moduleId"]!).ToString()).ToArray(),
            ["moduleTypes"] = modules.Select(module => (string)module["moduleName"]!).Distinct().ToArray()
        });
    }

    private void TrackSessionStartFailed(SessionPresetViewModel preset, Guid sessionInstanceId, string stage,
        string failureReason) => _telemetry?.TrackEvent("SessionStartFailed", new Dictionary<string, object?>
        {
            ["sessionInstanceId"] = sessionInstanceId,
            ["presetId"] = preset.Id,
            ["startSource"] = "manual",
            ["stage"] = stage,
            ["failureReason"] = failureReason,
            ["result"] = "failed"
        });

    private static string CommitmentMode(FocusCommitmentMode mode) => mode switch
    {
        FocusCommitmentMode.Locked => "locked",
        FocusCommitmentMode.Strict => "strict",
        _ => "normal"
    };

    private static string EndCondition(FocusEndCondition condition) => condition switch
    {
        FocusEndCondition.Duration => "duration",
        FocusEndCondition.EndAt => "end_at",
        _ => "none"
    };

    private static string AfterEndAction(AfterEndBehavior behavior) => behavior switch
    {
        AfterEndBehavior.StartNextWorkspace => "start_next_workspace",
        AfterEndBehavior.LockPc => "lock_pc",
        AfterEndBehavior.Sleep => "sleep",
        AfterEndBehavior.SignOut => "sign_out",
        AfterEndBehavior.ShutDownPc => "shut_down_pc",
        _ => "do_nothing"
    };

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
        var sessionInstanceId = _pendingStartSessionInstanceId ?? Guid.NewGuid();
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
            var preflight = await _sessionsApi.PreflightSessionAsync(preset.Id, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!preflight.Success)
            {
                TrackSessionStartFailed(preset, sessionInstanceId, "preflight", "validation_failed");
                var error = $"{preset.Model.FocusCommitment.Mode} Session cannot start: {preflight.Message}";
                _pendingStartPreset = null;
                _pendingStartSessionInstanceId = null;
                IsStartConfirmationOpen = false;
                _toastService?.Show(error, NotificationType.Error);
                return;
            }

            _pendingStartPreset = null;
            _pendingStartSessionInstanceId = null;
            IsStartConfirmationOpen = false;
            IsStartCountdownRunning = false;
            await LaunchPresetAsync(preset, sessionInstanceId);
        }
        catch (OperationCanceledException)
        {
            // The user cancelled during the ten-second countdown.
        }
        catch (Exception ex)
        {
            TrackSessionStartFailed(preset, sessionInstanceId, "preflight", "network_error");
            _telemetry?.TrackError(ex, "session", "session_start", "error", handled: true, fatal: false,
                properties: new Dictionary<string, object?>
                {
                    ["sessionInstanceId"] = sessionInstanceId,
                    ["presetId"] = preset.Id,
                    ["stage"] = "preflight"
                });
            throw;
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
        if (_pendingStartPreset is { } preset && _pendingStartSessionInstanceId is { } sessionInstanceId)
        {
            _telemetry?.TrackEvent("SessionStartCancelled", new Dictionary<string, object?>
            {
                ["sessionInstanceId"] = sessionInstanceId,
                ["presetId"] = preset.Id,
                ["startSource"] = "manual",
                ["result"] = "cancelled"
            });
        }

        _startCountdownCts?.Cancel();
        _startCountdownCts = null;
        _pendingStartPreset = null;
        _pendingStartSessionInstanceId = null;
        IsStartCountdownRunning = false;
        StartCountdownSeconds = 0;
        IsStartConfirmationOpen = false;
    }

    private async Task LaunchPresetAsync(SessionPresetViewModel presetVm, Guid sessionInstanceId)
    {
        try
        {
            SetActiveSessionPreset(presetVm.Id);
            ActiveFocusCommitmentMode = presetVm.Model.FocusCommitment.Mode;
            IsSessionActive = true;

            var result = await _sessionsApi.StartSessionAsync(presetVm.Id, sessionInstanceId);
            if (!result.Success)
            {
                var error = $"Failed to start session: {result.Message}";
                _toastService?.Show(error, NotificationType.Error);
                SetActiveSessionPreset(null);
                ActiveFocusCommitmentMode = FocusCommitmentMode.Normal;
                IsSessionActive = false;
            }
            else
            {
                _toastService?.Show($"Session '{presetVm.Name}' started.", NotificationType.Success);
                await RefreshSessionStateAsync();
            }
        }
        catch (Exception ex)
        {
            TrackSessionStartFailed(presetVm, sessionInstanceId, "rpc_request", "network_error");
            _telemetry?.TrackError(ex, "session", "session_start", "error", handled: true, fatal: false,
                properties: new Dictionary<string, object?>
                {
                    ["sessionInstanceId"] = sessionInstanceId,
                    ["presetId"] = presetVm.Id,
                    ["stage"] = "rpc_request"
                });
            var error = $"Failed to start session: {ex.Message}";
            _toastService?.Show(error, NotificationType.Error);
            SetActiveSessionPreset(null);
            ActiveFocusCommitmentMode = FocusCommitmentMode.Normal;
            IsSessionActive = false;
        }
    }

    private async Task StopCurrentSessionAsync()
    {
        try
        {
            var result = await _sessionsApi.StopSessionAsync();
            if (!result.Success)
            {
                _toastService?.Show($"Failed to stop session: {result.Message}", NotificationType.Error);
            }
            else
            {
                _toastService?.Show("Session stopped.", NotificationType.Success);
                await RefreshSessionStateAsync();
            }
        }
        catch (Exception ex)
        {
            _toastService?.Show($"Failed to stop session: {ex.Message}", NotificationType.Error);
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
                await Dispatcher.UIThread.InvokeAsync(() =>
                    _toastService?.Show("Emergency unlock completed.", NotificationType.Success));
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
            _toastService?.Show($"Preset '{presetVm.Name}' deleted.", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _toastService?.Show($"Failed to delete preset: {ex.Message}", NotificationType.Error);
        }
    }

    private async Task LoadPresetsAsync()
    {
        try
        {
            var presets = await _presetsApi.ListPresetsAsync();

            var modulesApi = _serviceProvider.GetRequiredService<IModulesApi>();
            var modules = await modulesApi.ListModulesAsync();

            IReadOnlyList<SessionSchedule> schedules = [];
            var schedulesAvailable = true;
            try
            {
                if (_serviceProvider.GetService<ISchedulerApi>() is { } schedulerApi)
                {
                    schedules = await schedulerApi.ListSchedulesAsync();
                }
            }
            catch (Exception)
            {
                schedulesAvailable = false;
            }

            var newVms = new List<SessionPresetViewModel>();
            var fullPresets = new List<SessionPreset>();
            foreach (var summary in presets)
            {
                var fullPreset = await _presetsApi.GetPresetAsync(summary.Id);
                if (fullPreset != null)
                {
                    fullPresets.Add(fullPreset);
                    newVms.Add(new SessionPresetViewModel(fullPreset, modules, modulesApi, _serviceProvider,
                        schedules.Where(schedule => schedule.PresetId == fullPreset.Id).ToArray()));
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
                    vm.IsActive = vm.Id == ActiveSessionPresetId;
                    Presets.Add(vm);
                }

                UpdateNextScheduledPreset(schedules, fullPresets, schedulesAvailable);
                UpdateDashboardActivity();

            });
        }
        catch (Exception ex)
        {
            _toastService?.Show($"Failed to load presets: {ex.Message}", NotificationType.Error);
        }
    }

    private void UpdateNextScheduledPreset(IReadOnlyList<SessionSchedule> schedules,
        IReadOnlyList<SessionPreset> presets, bool schedulesAvailable)
    {
        NextScheduledPresetName = string.Empty;
        NextScheduledTime = string.Empty;
        UpcomingScheduleStatus = schedulesAvailable ? "No scheduled preset." : "Unable to load schedules.";

        if (!schedulesAvailable)
        {
            this.RaisePropertyChanged(nameof(HasUpcomingSchedule));
            return;
        }

        var now = DateTimeOffset.Now;
        var next = schedules
            .Select(schedule => new { Schedule = schedule, NextRun = schedule.GetNextRun(now) })
            .Where(item => item.NextRun.HasValue)
            .OrderBy(item => item.NextRun)
            .FirstOrDefault();

        if (next is null || presets.FirstOrDefault(preset => preset.Id == next.Schedule.PresetId) is not { } preset)
        {
            this.RaisePropertyChanged(nameof(HasUpcomingSchedule));
            return;
        }

        NextScheduledPresetName = preset.Name;
        NextScheduledTime = next.NextRun!.Value.ToLocalTime().ToString("ddd, HH:mm");
        UpcomingScheduleStatus = string.Empty;
        this.RaisePropertyChanged(nameof(HasUpcomingSchedule));
    }

    private async Task RefreshSessionStateAsync()
    {
        try
        {
            var state = await _sessionsApi.GetCurrentSessionAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _sessionStateFailureNotified = false;
                if (state?.IsActive == true)
                {
                    SetActiveSessionPreset(state.PresetId);
                    ActiveFocusCommitmentMode = state.FocusCommitment;
                    IsSessionActive = true;
                    if (!string.IsNullOrWhiteSpace(state.PresetName))
                    {
                        ActiveWorkspaceName = state.PresetName;
                    }
                    BeginTrackingSession(state.PresetId, state.StartedAt, state.PresetName);
                    _activeSessionEndsAt = state.EndsAt;
                    _activeBreakEndsAt = state.BreakEndsAt;
                    _activeSessionRemainingDeadline = state.Remaining is { } sessionRemaining
                        ? GetStopwatchDeadline(sessionRemaining)
                        : null;
                    _activeBreakRemainingDeadline = state.BreakRemaining is { } breakRemaining
                        ? GetStopwatchDeadline(breakRemaining)
                        : null;
                    _activeBreaksRemaining = state.BreaksRemaining;
                    UpdateBreakPolicy(state.PresetId);
                    ActiveAfterEndBehavior = ToAfterEndLabel(state.AfterEnd);
                    ActiveProtectionStatus = ToProtectionLabel(state.ProtectionStatus);
                    this.RaisePropertyChanged(nameof(ActiveSessionMode));
                    UpdateCanStartBreak();
                    UpdateActiveSessionClock();
                }
                else
                {
                    EndTrackingSession(DateTimeOffset.Now);
                    SetActiveSessionPreset(null);
                    ActiveFocusCommitmentMode = FocusCommitmentMode.Normal;
                    IsSessionActive = false;
                    IsEmergencyUnlockOpen = false;
                    ResetActiveSessionDetails();
                }
            });
        }
        catch (Exception ex)
        {
            var message = $"Unable to fetch session state: {ex.Message}";
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_sessionStateFailureNotified)
                {
                    _toastService?.Show(message, NotificationType.Error);
                    _sessionStateFailureNotified = true;
                }

                SetActiveSessionPreset(null);
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
        try
        {
            var result = await _sessionsApi.StartBreakAsync();
            if (!result.Success)
            {
                _toastService?.Show($"Failed to start break: {result.Message}", NotificationType.Error);
                return;
            }

            _toastService?.Show("Break started.", NotificationType.Success);
            await RefreshSessionStateAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Show($"Failed to start break: {ex.Message}", NotificationType.Error);
        }
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

    private sealed record SessionActivity(DateTimeOffset StartedAt, DateTimeOffset EndedAt, string PresetName);

    private void BeginTrackingSession(Guid? presetId, DateTimeOffset? startedAt, string? presetName = null)
    {
        if (startedAt is not { } start)
        {
            return;
        }

        if (_trackedSessionStartedAt != start || _trackedSessionPresetId != presetId)
        {
            _trackedSessionStartedAt = start;
            _trackedSessionPresetId = presetId;
            _trackedSessionPresetName = string.Empty;
        }

        _trackedSessionPresetName = !string.IsNullOrWhiteSpace(presetName)
            ? presetName
            : Presets.FirstOrDefault(preset => preset.Id == presetId)?.Name ?? _trackedSessionPresetName;
        UpdateDashboardActivity(DateTimeOffset.Now);
    }

    private void EndTrackingSession(DateTimeOffset endedAt)
    {
        if (_trackedSessionStartedAt is { } startedAt && endedAt > startedAt)
        {
            var duration = endedAt - startedAt;
            _sessionActivity.RemoveAll(activity => activity.EndedAt < endedAt.AddDays(-7));
            _sessionActivity.Add(new SessionActivity(startedAt, endedAt, _trackedSessionPresetName));
            if (!string.IsNullOrWhiteSpace(_trackedSessionPresetName))
            {
                RecentSessions.Insert(0, $"{_trackedSessionPresetName} · {FormatDuration(duration)}");
                while (RecentSessions.Count > 5)
                {
                    RecentSessions.RemoveAt(RecentSessions.Count - 1);
                }

                this.RaisePropertyChanged(nameof(HasRecentSessions));
            }
        }

        _trackedSessionStartedAt = null;
        _trackedSessionPresetId = null;
        _trackedSessionPresetName = string.Empty;
        UpdateDashboardActivity(endedAt);
    }

    private void UpdateDashboardActivity(DateTimeOffset? timestamp = null)
    {
        var now = timestamp ?? DateTimeOffset.Now;
        if (timestamp is null && _lastDashboardActivityRefresh.Date == now.Date &&
            _lastDashboardActivityRefresh.Minute == now.Minute)
        {
            return;
        }

        _lastDashboardActivityRefresh = now;
        var startOfToday = StartOfLocalDay(now);
        var startOfWeek = now.AddDays(-6).Date;
        var activity = _sessionActivity.ToList();
        if (_trackedSessionStartedAt is { } startedAt)
        {
            activity.Add(new SessionActivity(startedAt, now, _trackedSessionPresetName));
        }

        FocusTodayLabel = FormatDuration(SumOverlap(activity, startOfToday, now));
        FocusThisWeekLabel = FormatDuration(SumOverlap(activity, new DateTimeOffset(startOfWeek,
            TimeZoneInfo.Local.GetUtcOffset(startOfWeek)), now));
        SessionsThisWeek = activity.Count(session => session.StartedAt >= now.AddDays(-7));
        FocusActivityValues = Enumerable.Range(0, 7)
            .Select(day =>
            {
                var dayStart = StartOfLocalDay(now.AddDays(day - 6));
                var dayEnd = StartOfLocalDay(dayStart.AddDays(1));
                if (dayEnd > now)
                {
                    dayEnd = now;
                }

                return SumOverlap(activity, dayStart, dayEnd).TotalMinutes;
            })
            .ToArray();
        this.RaisePropertyChanged(nameof(HasSessionActivity));
    }

    private static TimeSpan SumOverlap(IEnumerable<SessionActivity> sessions, DateTimeOffset start,
        DateTimeOffset end)
    {
        return sessions.Aggregate(TimeSpan.Zero,
            (total, session) => total + GetOverlap(session.StartedAt, session.EndedAt, start, end));
    }

    private static TimeSpan GetOverlap(DateTimeOffset sessionStart, DateTimeOffset sessionEnd,
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        var start = sessionStart > rangeStart ? sessionStart : rangeStart;
        var end = sessionEnd < rangeEnd ? sessionEnd : rangeEnd;
        return end > start ? end - start : TimeSpan.Zero;
    }

    private static DateTimeOffset StartOfLocalDay(DateTimeOffset value)
    {
        var localDate = value.LocalDateTime.Date;
        return new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var minutes = Math.Max(0, (int)duration.TotalMinutes);
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60} m" : $"{minutes} min";
    }

    private void UpdateBreakPolicy(Guid? presetId)
    {
        var duration = Presets.FirstOrDefault(preset => preset.Id == presetId)?.Model.FocusCommitment.BreakDuration;
        ActiveBreakPolicy = _activeBreaksRemaining switch
        {
            0 => "No breaks available",
            _ when duration is { } value => $"{_activeBreaksRemaining} × {FormatDuration(value)} available",
            _ => $"{_activeBreaksRemaining} available"
        };
    }

    private static string ToAfterEndLabel(AfterEndBehavior behavior) => behavior switch
    {
        AfterEndBehavior.StartNextWorkspace => "Start next preset",
        AfterEndBehavior.LockPc => "Lock PC",
        AfterEndBehavior.Sleep => "Sleep",
        AfterEndBehavior.SignOut => "Sign out",
        AfterEndBehavior.ShutDownPc => "Shut down PC",
        _ => "Do nothing"
    };

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
            var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            ActiveSessionRemaining = $"{seconds / 60:00}:{seconds % 60:00}";
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
        ActiveBreakPolicy = "No breaks available";
        ActiveAfterEndBehavior = string.Empty;
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
