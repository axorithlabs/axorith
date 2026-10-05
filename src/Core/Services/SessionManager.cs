using Autofac;
using System.Collections.Concurrent;
using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Core.Telemetry;
using Axorith.Sdk;
using Axorith.Sdk.Services;
using Axorith.Shared.Exceptions;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

public class SessionManager(
    IModuleRegistry moduleRegistry,
    ILogger<SessionManager> logger,
    TimeSpan validationTimeout,
    TimeSpan startupTimeout,
    TimeSpan shutdownTimeout,
    ITelemetryService telemetry,
    string? committedSessionPath = null,
    ICommitmentProtectionService? commitmentProtection = null,
    string? sessionHistoryPath = null,
    ISecureStorageService? secureStorage = null)
    : ISessionManager
{
    private const string PreflightFailureDataKey = "Axorith.SessionPreflight";
    private readonly SessionHistoryStore _history = new(sessionHistoryPath);
    public IReadOnlyList<SessionActivity> SessionHistory => _history.Read();

    private sealed record PersistedCommittedSession(SessionPreset Preset, DateTimeOffset StartedAt,
        DateTimeOffset EndDeadline, int BreaksUsed = 0, DateTimeOffset? BreakEndsAt = null);

    private readonly ConcurrentDictionary<Guid, byte> _reportedPreflightAttempts = new();

    private readonly string? _committedSessionPath = committedSessionPath;
    private readonly ICommitmentProtectionService? _commitmentProtection = commitmentProtection;
    private readonly ISecureStorageService? _secureStorage = secureStorage;
    private static readonly JsonSerializerOptions CommittedSessionJsonOptions = new() { WriteIndented = true };
    private CancellationTokenSource? _sessionCts;
    private readonly SemaphoreSlim _asyncLock = new(1, 1);
    private readonly object _syncLock = new();
    private int _breaksUsed;
    private long? _sessionEndsAtTimestamp;
    private long? _breakEndsAtTimestamp;
    private bool _protectionWasUnavailable;
    private Guid? _sessionInstanceId;
    private Guid? _previousSessionInstanceId;
    private Guid? _sessionScheduleId;
    private string _sessionStartSource = "manual";
    private bool _sessionTelemetryStarted;
    private string _protectionStatus = "Protection active";

    private class ActiveModule : IDisposable
    {
        public required IModule Instance { get; init; }
        public required ILifetimeScope Scope { get; init; }
        public required ConfiguredModule Configuration { get; init; }
        public required ModuleDefinition Definition { get; init; }
        public bool IsStarted { get; set; }

        public string DisplayName => Configuration.CustomName ?? Definition.Name;

        public void Dispose()
        {
            try
            {
                Instance.Dispose();
            }
            finally
            {
                Scope.Dispose();
            }
        }
    }

    private readonly List<ActiveModule> _activeModules = [];

    public bool IsSessionRunning => ActiveSession != null;
    public SessionPreset? ActiveSession { get; private set; }
    public Guid? CurrentSessionInstanceId => _sessionInstanceId;
    public DateTimeOffset? SessionStartedAt { get; private set; }
    public DateTimeOffset? SessionEndsAt { get; private set; }
    public TimeSpan? SessionTimeRemaining => SessionEndsAt.HasValue ? GetRemainingSessionTime() : null;
    public DateTimeOffset? BreakEndsAt { get; private set; }
    public int BreaksRemaining => Math.Max(0, (ActiveSession?.FocusCommitment.BreakCount ?? 0) - _breaksUsed);
    public TimeSpan? BreakTimeRemaining => _breakEndsAtTimestamp is { } deadline
        ? MonotonicTime.RemainingUntil(deadline)
        : null;
    public string ProtectionStatus
    {
        get => _protectionStatus;
        private set
        {
            var previousState = GetProtectionState(_protectionStatus);
            _protectionStatus = value;
            var state = GetProtectionState(value);
            if (previousState != state && ActiveSession is { } session && _sessionTelemetryStarted)
            {
                telemetry.TrackEvent("ProtectionStateChanged", new Dictionary<string, object?>
                {
                    ["sessionInstanceId"] = _sessionInstanceId,
                    ["presetId"] = session.Id,
                    ["protectionState"] = state
                });
            }
        }
    }

    public event Action<Guid>? SessionStarted;
    public event Action<Guid>? SessionStopped;

    public async Task RecoverCommittedSessionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_committedSessionPath) || !File.Exists(_committedSessionPath))
        {
            return;
        }

        PersistedCommittedSession? state;
        try
        {
            using var document = JsonDocument.Parse(CommittedSessionStateFile.ReadPayload(_committedSessionPath));
            state = document.RootElement.Deserialize<PersistedCommittedSession>(CommittedSessionJsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not verify committed session recovery state; refusing to discard it.");
            throw new SessionException($"Committed session recovery state is invalid and was retained: {ex.Message}");
        }

        if (state?.Preset?.FocusCommitment == null ||
            !state.Preset.FocusCommitment.IsCommitted)
        {
            throw new SessionException("Committed session recovery state is invalid and was retained.");
        }

        var strictStateVerified = false;
        if (state.Preset.FocusCommitment.IsStrict)
        {
            if (_commitmentProtection == null)
            {
                throw new SessionException("Strict Windows protection is unavailable for recovery.");
            }

            await _commitmentProtection.CheckRecoveryStateAsync(cancellationToken).ConfigureAwait(false);
            strictStateVerified = true;
        }

        if (state.EndDeadline <= DateTimeOffset.UtcNow)
        {
            logger.LogInformation("Committed session {SessionId} expired while Host was stopped; clearing recovery state.",
                state.Preset.Id);
            DeleteCommittedSessionState();
            return;
        }

        logger.LogInformation("Restoring committed session '{Name}' from persisted Host state.", state.Preset.Name);
        try
        {
            await StartSessionWithTelemetryAsync(state.Preset, state.StartedAt, state.EndDeadline, state,
                    cancellationToken, "recovered", null, null, null)
                .ConfigureAwait(false);
        }
        catch
        {
            var strictStatePath = Path.Combine(Path.GetDirectoryName(_committedSessionPath)!, "strict-protection.json");
            if (strictStateVerified && !File.Exists(strictStatePath))
            {
                DeleteCommittedSessionState();
                if (_commitmentProtection != null)
                {
                    await _commitmentProtection.SetRecoveryStartupAsync(false, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                throw;
            }

            _breaksUsed = state.BreaksUsed;
            BreakEndsAt = state.BreakEndsAt;
            SaveCommittedSessionState(state.Preset, state.StartedAt, state.EndDeadline);
            if (_commitmentProtection != null)
            {
                try
                {
                    await _commitmentProtection.SetRecoveryStartupAsync(true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception startupError)
                {
                    logger.LogError(startupError,
                        "Could not preserve Windows sign-in recovery after committed session startup failed.");
                }
            }

            throw;
        }
    }

    public async Task PreflightSessionAsync(SessionPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset.FocusCommitment ??= new FocusCommitmentOptions();
        var snapshot = new SessionPreset(preset);
        _ = GetSessionEnd(snapshot.FocusCommitment, DateTimeOffset.Now);
        if (snapshot.FocusCommitment.IsStrict)
        {
            if (_commitmentProtection == null)
            {
                throw new SessionException("Strict Session cannot start because Windows protection is unavailable on this Host.");
            }

            try
            {
                await _commitmentProtection.CheckCanEnableAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new SessionException($"Strict Session cannot start. {ex.Message}");
            }
        }

        if (snapshot.FocusCommitment.IsCommitted)
        {
            CheckCommittedStateStorage();
            if (_commitmentProtection != null)
            {
                try
                {
                    await _commitmentProtection.CheckCanSetRecoveryStartupAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new SessionException($"Committed Session cannot start. {ex.Message}");
                }
            }
        }

        await _asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var modules = new List<ActiveModule>();
        try
        {
            if (IsSessionRunning)
            {
                throw new SessionException("A session is already running.");
            }

            foreach (var configuredModule in snapshot.Modules)
            {
                var module = CreateActiveModule(configuredModule) ??
                             throw new SessionException($"Module {configuredModule.ModuleId} could not be loaded.");
                modules.Add(module);
            }

            if (modules.Count == 0)
            {
                throw new SessionException($"Workspace '{snapshot.Name}' has no modules to start.");
            }

            ConfigureWorkspaceApplications(modules);
            await ValidateAllModulesAsync(modules, cancellationToken).ConfigureAwait(false);
            if (snapshot.FocusCommitment.IsCommitted)
            {
                await ValidateCommittedModuleSetupAsync(modules, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var module in modules)
            {
                try
                {
                    module.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing preflight module '{Name}'.", module.DisplayName);
                }
            }

            _asyncLock.Release();
        }
    }

    public Task StartSessionAsync(SessionPreset preset, CancellationToken cancellationToken = default,
        string startSource = "manual", Guid? sessionInstanceId = null, Guid? scheduleId = null,
        Guid? previousSessionInstanceId = null)
    {
        return StartSessionWithTelemetryAsync(preset, null, null, null, cancellationToken, startSource,
            sessionInstanceId, scheduleId, previousSessionInstanceId);
    }

    private async Task StartSessionWithTelemetryAsync(SessionPreset preset, DateTimeOffset? startedAtOverride,
        DateTimeOffset? endAtOverride, PersistedCommittedSession? recoveryState, CancellationToken cancellationToken,
        string startSource, Guid? sessionInstanceId, Guid? scheduleId, Guid? previousSessionInstanceId)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var instanceId = sessionInstanceId ?? Guid.NewGuid();

        try
        {
            await StartSessionInternalAsync(preset, startedAtOverride, endAtOverride, recoveryState,
                    cancellationToken, instanceId, startSource, scheduleId, previousSessionInstanceId)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var isPreflightFailure = ex.Data.Contains(PreflightFailureDataKey);
            TrackSessionStartFailed(preset.Id, instanceId, startSource, ex);
            telemetry.TrackError(ex, "session", "session_start", "error", handled: true, fatal: false,
                properties: new Dictionary<string, object?>
                {
                    ["sessionInstanceId"] = instanceId,
                    ["presetId"] = preset.Id,
                    ["stage"] = isPreflightFailure ? "preflight" : "session_initialization",
                    ["failureReason"] = ProductAnalyticsProperties.FailureReason(ex)
                });
            throw;
        }
    }

    private async Task StartSessionInternalAsync(SessionPreset preset, DateTimeOffset? startedAtOverride,
        DateTimeOffset? endAtOverride, PersistedCommittedSession? recoveryState, CancellationToken cancellationToken,
        Guid sessionInstanceId, string startSource, Guid? scheduleId, Guid? previousSessionInstanceId)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset.FocusCommitment ??= new FocusCommitmentOptions();
        var snapshot = new SessionPreset(preset);
        ValidateFocusCommitment(snapshot.FocusCommitment);
        var sessionEndsAt = endAtOverride ?? GetSessionEnd(snapshot.FocusCommitment, DateTimeOffset.Now);

        if (snapshot.FocusCommitment.IsCommitted)
        {
            try
            {
                await PreflightSessionAsync(snapshot, cancellationToken).ConfigureAwait(false);
                TrackSessionPreflightCompleted(sessionInstanceId, snapshot.Id, startSource, "success");
            }
            catch (Exception ex)
            {
                TrackSessionPreflightCompleted(sessionInstanceId, snapshot.Id, startSource, "failed",
                    ProductAnalyticsProperties.FailureReason(ex));
                ex.Data[PreflightFailureDataKey] = true;
                throw;
            }
        }

        try
        {
            await _asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsSessionRunning)
                {
                    throw new SessionException(
                        "A session is already running. Stop the current session before starting a new one.");
                }

                logger.LogInformation("Initializing session '{PresetName}'...", snapshot.Name);

                ActiveSession = snapshot;
                _sessionInstanceId = sessionInstanceId;
                _previousSessionInstanceId = previousSessionInstanceId;
                _sessionScheduleId = scheduleId;
                _sessionStartSource = startSource;
                _sessionTelemetryStarted = false;
                SessionStartedAt = startedAtOverride ?? DateTimeOffset.UtcNow;
                SessionEndsAt = sessionEndsAt;
                _breaksUsed = recoveryState?.BreaksUsed ?? 0;
                BreakEndsAt = recoveryState?.BreakEndsAt > DateTimeOffset.UtcNow ? recoveryState.BreakEndsAt : null;
                _breakEndsAtTimestamp = BreakEndsAt is { } breakEnd
                    ? MonotonicTime.DeadlineAfter(breakEnd - DateTimeOffset.UtcNow)
                    : null;
                _sessionEndsAtTimestamp = sessionEndsAt is { } end
                    ? MonotonicTime.DeadlineAfter(end - DateTimeOffset.UtcNow)
                    : null;
                ProtectionStatus = snapshot.FocusCommitment.Mode == FocusCommitmentMode.Normal
                    ? "Protection inactive"
                    : "Protection active";
                _protectionWasUnavailable = false;
                if (snapshot.FocusCommitment.IsCommitted)
                {
                    if (_commitmentProtection != null)
                    {
                        try
                        {
                            await _commitmentProtection.SetRecoveryStartupAsync(true, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            throw new SessionException($"Committed Session cannot start. {ex.Message}");
                        }
                    }

                    SaveCommittedSessionState(snapshot, SessionStartedAt.Value, SessionEndsAt!.Value);
                }

                if (snapshot.FocusCommitment.IsStrict)
                {
                    if (_commitmentProtection == null)
                    {
                        throw new SessionException("Strict Session cannot start because Windows protection is unavailable on this Host.");
                    }

                    try
                    {
                        await _commitmentProtection.EnableAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            await _commitmentProtection.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception restoreError)
                        {
                            logger.LogError(restoreError, "Failed to roll back partial Strict protection setup.");
                        }

                        throw new SessionException($"Strict Session cannot start. Axorith could not enable Windows protection: {ex.Message}");
                    }
                }

                _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeModules.Clear();

                foreach (var configuredModule in snapshot.Modules)
                {
                    var activeModule = CreateActiveModule(configuredModule);
                    if (activeModule is not null)
                    {
                        _activeModules.Add(activeModule);
                        continue;
                    }

                    logger.LogWarning(
                        "Failed to create instance for module {ModuleId} in preset '{PresetName}'. Skipping.",
                        configuredModule.ModuleId, snapshot.Name);
                }

                if (_activeModules.Count == 0)
                {
                    throw new SessionException($"No modules could be instantiated for preset '{snapshot.Name}'. Aborting.");
                }

                ConfigureWorkspaceApplications(_activeModules);
            }
            finally
            {
                _asyncLock.Release();
            }

            await ValidateAllModulesAsync(_activeModules, _sessionCts.Token).ConfigureAwait(false);

            await RunHybridStartupAsync(_activeModules, _sessionCts.Token).ConfigureAwait(false);

            if (recoveryState?.BreakEndsAt is { } recoveredBreakEnd && recoveredBreakEnd > DateTimeOffset.UtcNow)
            {
                foreach (var module in _activeModules)
                {
                    if (module.Instance is ISessionBreakParticipant participant)
                    {
                        await participant.PauseForBreakAsync(_sessionCts.Token).ConfigureAwait(false);
                    }
                }
            }

            if (snapshot.FocusCommitment.IsCommitted)
            {
                await ValidateCommittedProtectionAsync(_activeModules, _sessionCts.Token).ConfigureAwait(false);
                if (snapshot.FocusCommitment.IsStrict &&
                    (_commitmentProtection == null ||
                     !await _commitmentProtection.IsEnabledAsync(_sessionCts.Token).ConfigureAwait(false)))
                {
                    throw new SessionException("Strict Windows protection is not active.");
                }

                _protectionWasUnavailable = false;
                UpdateHealthyProtectionStatus();
            }

            logger.LogInformation("Session '{PresetName}' started successfully with {Count} modules.",
                snapshot.Name, _activeModules.Count);
            SessionStarted?.Invoke(snapshot.Id);
            TrackSessionStarted(snapshot, _activeModules);
            _sessionTelemetryStarted = true;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(ActiveSession, snapshot))
            {
                logger.LogError(ex, "Session startup failed. Initiating rollback...");
                await StopSessionAsync(SessionEndReason.StartupFailure, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    private ActiveModule? CreateActiveModule(ConfiguredModule configuration)
    {
        var (instance, scope) = moduleRegistry.CreateInstance(configuration.ModuleId);
        if (instance is null || scope is null)
        {
            instance?.Dispose();
            scope?.Dispose();
            return null;
        }

        try
        {
            var module = new ActiveModule
            {
                Instance = instance,
                Scope = scope,
                Configuration = configuration,
                Definition = scope.Resolve<ModuleDefinition>()
            };
            ApplySettings(module);
            return module;
        }
        catch
        {
            try
            {
                instance.Dispose();
            }
            finally
            {
                scope.Dispose();
            }

            throw;
        }
    }

    private void ApplySettings(ActiveModule module)
    {
        var moduleSettings = module.Instance.GetSettings().ToDictionary(s => s.Key);
        foreach (var savedSetting in module.Configuration.Settings)
        {
            if (moduleSettings.TryGetValue(savedSetting.Key, out var setting))
            {
                setting.SetValueFromString(savedSetting.Value);
            }
        }
    }

    private async Task ValidateAllModulesAsync(List<ActiveModule> modules, CancellationToken ct)
    {
        logger.LogInformation("Validating {Count} modules...", modules.Count);

        var validationTasks = modules.Select(async module =>
        {
            using var validationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            validationCts.CancelAfter(validationTimeout);

            try
            {
                var result = await module.Instance.ValidateSettingsAsync(validationCts.Token).ConfigureAwait(false);
                if (result.Status == ValidationStatus.Error)
                {
                    throw new SessionException($"Module '{module.DisplayName}' validation failed: {result.Message}");
                }

                if (result.Status == ValidationStatus.Warning && _sessionInstanceId.HasValue)
                {
                    var properties = ModuleTelemetryProperties(module);
                    properties["stage"] = "module_validation";
                    properties["result"] = "warning";
                    properties["failureReason"] = "validation_warning";
                    telemetry.TrackEvent("SessionValidationWarning", properties);
                }
            }
            catch (OperationCanceledException)
            {
                throw new SessionException($"Module '{module.DisplayName}' validation timed out.");
            }
        });

        await Task.WhenAll(validationTasks).ConfigureAwait(false);
    }

    private async Task RunHybridStartupAsync(List<ActiveModule> modules, CancellationToken ct)
    {
        logger.LogInformation("Starting modules (Hybrid Mode)...");

        var batch = new List<ActiveModule>();

        foreach (var currentModule in modules)
        {
            if (currentModule.Configuration.StartDelay > TimeSpan.Zero)
            {
                // Flush current batch first
                if (batch.Count > 0)
                {
                    await ExecuteBatchAsync(batch, ct).ConfigureAwait(false);
                    batch.Clear();
                }

                logger.LogInformation("Waiting {Delay}s before starting '{Name}'...",
                    currentModule.Configuration.StartDelay.TotalSeconds, currentModule.DisplayName);

                await Task.Delay(currentModule.Configuration.StartDelay, ct).ConfigureAwait(false);

                await StartSingleModuleAsync(currentModule, ct).ConfigureAwait(false);
            }
            else
            {
                batch.Add(currentModule);
            }
        }

        if (batch.Count > 0)
        {
            await ExecuteBatchAsync(batch, ct).ConfigureAwait(false);
        }
    }

    private async Task ExecuteBatchAsync(List<ActiveModule> batch, CancellationToken ct)
    {
        switch (batch.Count)
        {
            case 0:
                return;
            case 1:
                await StartSingleModuleAsync(batch[0], ct).ConfigureAwait(false);
                return;
        }

        logger.LogInformation("Starting batch of {Count} modules in parallel...", batch.Count);

        var tasks = batch.Select(m => StartSingleModuleAsync(m, ct));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task StartSingleModuleAsync(ActiveModule module, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = ActiveSession?.Id ?? Guid.Empty,
            ["ModuleName"] = module.Definition.Name,
            ["ModuleInstanceName"] = module.DisplayName
        });

        logger.LogInformation("Starting module '{InstanceName}'...", module.DisplayName);

        try
        {
            using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            startCts.CancelAfter(startupTimeout);

            await module.Instance.OnSessionStartAsync(startCts.Token).ConfigureAwait(false);

            logger.LogInformation("Module '{InstanceName}' started successfully.", module.DisplayName);
            module.IsStarted = true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out specifically
            logger.LogError("Module '{InstanceName}' startup timed out after {Timeout}s.",
                module.DisplayName, startupTimeout.TotalSeconds);
            TrackModuleFailed(module, "timeout",
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw new SessionException($"Module '{module.DisplayName}' startup timed out.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Module '{InstanceName}' failed to start.", module.DisplayName);
            TrackModuleFailed(module, "unknown",
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw new SessionException($"Module '{module.DisplayName}' failed to start: {ex.Message}");
        }
    }

    public Task StopCurrentSessionAsync(CancellationToken cancellationToken = default) =>
        StopSessionAsync(SessionEndReason.UserStop, cancellationToken);

    public Task<bool> EndCommittedSessionAsync(SessionEndReason reason, CancellationToken cancellationToken = default)
    {
        if (reason is not (SessionEndReason.NaturalCompletion or SessionEndReason.EmergencyUnlock))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return StopSessionAsync(reason, cancellationToken);
    }

    public async Task StartBreakAsync(CancellationToken cancellationToken = default)
    {
        var breakStartAttempted = false;
        try
        {
            await _asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var commitment = ActiveSession?.FocusCommitment;
                if (commitment?.IsCommitted != true)
                {
                    throw new SessionException("Breaks are only available during a committed session.");
                }

                if (BreakEndsAt.HasValue)
                {
                    throw new SessionException("A break is already in progress.");
                }

                if (BreaksRemaining <= 0)
                {
                    throw new SessionException("No breaks remain in this session.");
                }

                var remainingSession = GetRemainingSessionTime();
                if (commitment.BreakDuration <= TimeSpan.Zero || remainingSession < commitment.BreakDuration)
                {
                    throw new SessionException("A break must fit entirely before the session ends.");
                }

                breakStartAttempted = true;
                var nextBreakEnd = DateTimeOffset.UtcNow.Add(commitment.BreakDuration);
                _breaksUsed++;
                BreakEndsAt = nextBreakEnd;
                _breakEndsAtTimestamp = MonotonicTime.DeadlineAfter(commitment.BreakDuration);
                try
                {
                    SaveCommittedSessionState(ActiveSession!, SessionStartedAt!.Value, SessionEndsAt!.Value);
                }
                catch
                {
                    _breaksUsed--;
                    BreakEndsAt = null;
                    _breakEndsAtTimestamp = null;
                    throw;
                }

                var paused = new List<ISessionBreakParticipant>();
                try
                {
                    foreach (var module in _activeModules)
                    {
                        if (module.Instance is not ISessionBreakParticipant participant)
                        {
                            continue;
                        }

                        paused.Add(participant);
                        await participant.PauseForBreakAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (_activeModules.Any(module =>
                            module.Instance is ICommittedSessionValidator { IsProtectionDegraded: true }))
                    {
                        ProtectionStatus = BuildProtectionStatus("Protection degraded", _activeModules);
                        _protectionWasUnavailable = true;
                    }

                    telemetry.TrackEvent("SessionBreakStarted", new Dictionary<string, object?>
                    {
                        ["sessionInstanceId"] = _sessionInstanceId,
                        ["presetId"] = ActiveSession?.Id,
                        ["breakCount"] = commitment.BreakCount,
                        ["breaksUsed"] = _breaksUsed,
                        ["breakDurationMs"] = (long)commitment.BreakDuration.TotalMilliseconds,
                        ["result"] = "started"
                    });
                }
                catch
                {
                    foreach (var participant in paused)
                    {
                        try
                        {
                            await participant.ResumeAfterBreakAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to restore a blocker after break startup failed.");
                        }
                    }

                    _breaksUsed--;
                    BreakEndsAt = null;
                    _breakEndsAtTimestamp = null;
                    ProtectionStatus = "Protection degraded";
                    _protectionWasUnavailable = true;
                    SaveCommittedSessionState(ActiveSession!, SessionStartedAt!.Value, SessionEndsAt!.Value);
                    throw;
                }
            }
            finally
            {
                _asyncLock.Release();
            }
        }
        catch (Exception ex)
        {
            if (breakStartAttempted)
            {
                telemetry.TrackEvent("SessionBreakStartFailed", new Dictionary<string, object?>
                {
                    ["sessionInstanceId"] = _sessionInstanceId,
                    ["presetId"] = ActiveSession?.Id,
                    ["breaksUsed"] = _breaksUsed,
                    ["result"] = "failed",
                    ["failureReason"] = ProductAnalyticsProperties.FailureReason(ex)
                });
            }
            throw;
        }
    }

    private static void ConfigureWorkspaceApplications(IEnumerable<ActiveModule> modules)
    {
        var moduleList = modules.ToArray();
        var processNames = moduleList
            .Where(module => module.Definition.Name == "Application Launcher")
            .Select(module =>
            {
                var settings = module.Configuration.Settings;
                var applicationPath = settings.GetValueOrDefault("ApplicationPath");
                return string.Equals(applicationPath, "custom-app", StringComparison.OrdinalIgnoreCase)
                    ? settings.GetValueOrDefault("CustomPath")
                    : applicationPath;
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var module in moduleList)
        {
            if (module.Instance is IWorkspaceApplicationAllowlist allowlist)
            {
                allowlist.SetWorkspaceApplications(processNames);
            }
        }
    }

    public async Task EndBreakAsync(CancellationToken cancellationToken = default)
    {
        await _asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!BreakEndsAt.HasValue)
            {
                return;
            }

            Exception? resumeError = null;
            foreach (var module in _activeModules)
            {
                if (module.Instance is not ISessionBreakParticipant participant)
                {
                    continue;
                }

                try
                {
                    await participant.ResumeAfterBreakAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    resumeError ??= ex;
                    logger.LogError(ex, "Failed to resume a blocker after the session break.");
                }
            }

            BreakEndsAt = null;
            _breakEndsAtTimestamp = null;
            if (resumeError != null)
            {
                ProtectionStatus = "Protection failed";
                _protectionWasUnavailable = true;
            }
            else
            {
                UpdateHealthyProtectionStatus();
            }

            if (ActiveSession?.FocusCommitment.IsCommitted == true)
            {
                SaveCommittedSessionState(ActiveSession, SessionStartedAt!.Value, SessionEndsAt!.Value);
            }

            telemetry.TrackEvent("SessionBreakEnded", new Dictionary<string, object?>
            {
                ["sessionInstanceId"] = _sessionInstanceId,
                ["presetId"] = ActiveSession?.Id,
                ["breaksUsed"] = _breaksUsed,
                ["result"] = resumeError is null ? "completed" : "failed"
            });

            if (resumeError != null)
            {
                throw new SessionException($"A blocker could not be restored after the break: {resumeError.Message}");
            }
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    public async Task RefreshProtectionHealthAsync(CancellationToken cancellationToken = default)
    {
        await _asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ActiveSession?.FocusCommitment.IsCommitted != true)
            {
                ProtectionStatus = "Protection inactive";
                _protectionWasUnavailable = false;
                return;
            }

            try
            {
                await ValidateCommittedProtectionAsync(_activeModules, cancellationToken).ConfigureAwait(false);
                if (ActiveSession.FocusCommitment.IsStrict)
                {
                    if (_commitmentProtection == null)
                    {
                        throw new SessionException("Strict Windows protection is unavailable.");
                    }

                    if (!await _commitmentProtection.IsEnabledAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await _commitmentProtection.EnableAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!await _commitmentProtection.IsEnabledAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new SessionException("Strict Windows protection is unavailable.");
                    }
                }

                UpdateHealthyProtectionStatus();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Committed session protection health check failed.");
                ProtectionStatus = BuildProtectionStatus("Protection degraded", _activeModules);
                _protectionWasUnavailable = true;
            }
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    private static Task ValidateCommittedProtectionAsync(List<ActiveModule> modules, CancellationToken cancellationToken) =>
        ValidateCommittedModulesAsync(modules,
            static (validator, token) => validator.IsProtectionHealthyAsync(token),
            "is disconnected or not protecting this session.", cancellationToken);

    private static Task ValidateCommittedModuleSetupAsync(List<ActiveModule> modules, CancellationToken cancellationToken) =>
        ValidateCommittedModulesAsync(modules,
            static (validator, token) => validator.CanStartCommittedSessionAsync(token),
            "is disconnected or unavailable for this session.", cancellationToken);

    private static async Task ValidateCommittedModulesAsync(List<ActiveModule> modules,
        Func<ICommittedSessionValidator, CancellationToken, Task<bool>> validate, string failureMessage,
        CancellationToken cancellationToken)
    {
        foreach (var module in modules)
        {
            if (module.Instance is not ICommittedSessionValidator validator ||
                await validate(validator, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var details = validator.ProtectionStatusMessage;
            throw new SessionException(string.IsNullOrWhiteSpace(details)
                ? $"{module.DisplayName} {failureMessage}"
                : $"{module.DisplayName}: {details}");
        }
    }

    private void UpdateHealthyProtectionStatus()
    {
        if (_activeModules.Any(module =>
                module.Instance is ICommittedSessionValidator { IsProtectionDegraded: true }))
        {
            ProtectionStatus = BuildProtectionStatus("Protection degraded", _activeModules);
            _protectionWasUnavailable = true;
            return;
        }

        ProtectionStatus = BuildProtectionStatus(
            _protectionWasUnavailable ? "Protection recovered" : "Protection active", _activeModules);
        _protectionWasUnavailable = false;
    }

    private static string BuildProtectionStatus(string state, IEnumerable<ActiveModule> modules)
    {
        var details = string.Join(" · ", modules
            .Select(module => (module.Instance as ICommittedSessionValidator)?.ProtectionStatusMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message)));
        return string.IsNullOrWhiteSpace(details) ? state : $"{state} · {details}";
    }

    private TimeSpan GetRemainingSessionTime() => _sessionEndsAtTimestamp is { } deadline
        ? MonotonicTime.RemainingUntil(deadline)
        : TimeSpan.Zero;

    private async Task<bool> StopSessionAsync(SessionEndReason reason, CancellationToken cancellationToken)
    {
        await _asyncLock.WaitAsync(cancellationToken);
        try
        {
            if (reason == SessionEndReason.UserStop &&
                ActiveSession?.FocusCommitment.IsCommitted == true)
            {
                throw new SessionException("This committed session cannot be stopped. Use Emergency Unlock to end it early.");
            }

            if (!IsSessionRunning && _activeModules.Count == 0)
            {
                logger.LogWarning("No session is currently running.");
                return false;
            }

            logger.LogInformation("Stopping current session...");

            var wasCommittedSession = ActiveSession?.FocusCommitment.IsCommitted == true;
            if (ActiveSession?.FocusCommitment.IsStrict == true)
            {
                if (_commitmentProtection == null)
                {
                    throw new SessionException("Session remains committed because Windows protection is unavailable.");
                }

                try
                {
                    await _commitmentProtection.RestoreAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new SessionException(
                        $"Session remains committed because Windows protection could not be restored: {ex.Message}");
                }
            }

            // Signal cancellation to any running startup tasks immediately
            _sessionCts?.Cancel();

            // Stop in reverse order so dependencies stay alive during teardown.
            for (var i = _activeModules.Count - 1; i >= 0; i--)
            {
                var activeModule = _activeModules[i];

                using var scope = logger.BeginScope(new Dictionary<string, object>
                {
                    ["ModuleInstanceName"] = activeModule.DisplayName
                });

                logger.LogInformation("Stopping module '{InstanceName}'...", activeModule.DisplayName);

                try
                {
                    using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    shutdownCts.CancelAfter(shutdownTimeout);

                    await activeModule.Instance.OnSessionEndAsync(shutdownCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // We swallow exceptions during stop to ensure we try to stop EVERYTHING.
                    logger.LogError(ex, "Module '{InstanceName}' threw an exception during stop.",
                        activeModule.DisplayName);
                    if (activeModule.IsStarted)
                    {
                        telemetry.TrackError(ex, "module", "module_execution", "error", handled: true,
                            fatal: false, properties: ModuleTelemetryProperties(activeModule));
                    }
                }
            }

            foreach (var activeModule in _activeModules)
            {
                try
                {
                    activeModule.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error disposing module scope for '{InstanceName}'.", activeModule.DisplayName);
                }
            }

            var stoppedPreset = ActiveSession;
            var stoppedPresetId = stoppedPreset?.Id ?? Guid.Empty;
            var stoppedModules = _activeModules.ToList();
            var startedAt = SessionStartedAt;
            var stoppedSessionInstanceId = _sessionInstanceId;
            var stoppedPreviousSessionInstanceId = _previousSessionInstanceId;
            var stoppedScheduleId = _sessionScheduleId;
            var startSource = _sessionStartSource;
            var breaksUsed = _breaksUsed;
            var reportSessionStop = _sessionTelemetryStarted;
            var emergencyUnlockUsed = reason == SessionEndReason.EmergencyUnlock;
            var protectionDegraded = _protectionWasUnavailable || GetProtectionState(ProtectionStatus) == "degraded";
            if (wasCommittedSession && _commitmentProtection != null)
            {
                try
                {
                    await _commitmentProtection.SetRecoveryStartupAsync(false, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Could not remove the recovery startup entry after the committed session ended.");
                }
            }

            if (reason != SessionEndReason.StartupFailure && startedAt is { } sessionStart && ActiveSession is { } completedSession)
            {
                try
                {
                    _history.Add(new SessionActivity(sessionStart, DateTimeOffset.UtcNow, completedSession.Name));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not persist session history.");
                }
            }
            CleanupSessionState();

            logger.LogInformation("Session stopped successfully.");

            if (stoppedPresetId != Guid.Empty)
            {
                SessionStopped?.Invoke(stoppedPresetId);
                if (reportSessionStop)
                {
                    TrackSessionStopped(stoppedPreset!, stoppedModules, startedAt, stoppedSessionInstanceId,
                        startSource, reason, breaksUsed, emergencyUnlockUsed, protectionDegraded, stoppedScheduleId,
                        stoppedPreviousSessionInstanceId);
                    if (emergencyUnlockUsed)
                    {
                        telemetry.TrackEvent("EmergencyUnlockUsed", new Dictionary<string, object?>
                        {
                            ["sessionInstanceId"] = stoppedSessionInstanceId,
                            ["presetId"] = stoppedPresetId,
                            ["result"] = "completed"
                        });
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Critical error during session stop.");
            throw;
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    private void CleanupSessionState()
    {
        _activeModules.Clear();
        _sessionCts?.Dispose();
        _sessionCts = null;
        ActiveSession = null;
        SessionStartedAt = null;
        SessionEndsAt = null;
        BreakEndsAt = null;
        _breaksUsed = 0;
        _sessionEndsAtTimestamp = null;
        _breakEndsAtTimestamp = null;
        _sessionInstanceId = null;
        _previousSessionInstanceId = null;
        _sessionScheduleId = null;
        _sessionStartSource = "manual";
        _sessionTelemetryStarted = false;
        ProtectionStatus = "Protection inactive";
        _protectionWasUnavailable = false;
        DeleteCommittedSessionState();
    }

    private void SaveCommittedSessionState(SessionPreset preset, DateTimeOffset startedAt, DateTimeOffset endDeadline)
    {
        var path = _committedSessionPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new SessionException("Committed session recovery state path is not configured.");
        }

        _ = GetCommittedStateDirectory();

        try
        {
            CommittedSessionStateFile.WritePayload(path, JsonSerializer.Serialize(
                new PersistedCommittedSession(preset, startedAt, endDeadline, _breaksUsed, BreakEndsAt),
                CommittedSessionJsonOptions));
        }
        catch (Exception ex)
        {
            throw new SessionException($"Committed session recovery state could not be saved: {ex.Message}");
        }
    }

    private void DeleteCommittedSessionState()
    {
        if (string.IsNullOrWhiteSpace(_committedSessionPath))
        {
            return;
        }

        try
        {
            File.Delete(_committedSessionPath);
            File.Delete(_committedSessionPath + ".tmp");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear committed session recovery state.");
        }
    }

    private static DateTimeOffset? GetSessionEnd(FocusCommitmentOptions options, DateTimeOffset localNow)
    {
        ValidateFocusCommitment(options);
        if (options.Mode == FocusCommitmentMode.Normal)
        {
            return null;
        }

        if (!options.IsCommitted)
        {
            throw new SessionException("Invalid Focus commitment mode.");
        }

        return options.EndCondition switch
        {
            FocusEndCondition.Duration when options.Duration is { } duration && duration > TimeSpan.Zero =>
                DateTimeOffset.UtcNow.Add(duration),
            FocusEndCondition.EndAt when options.EndAtLocalTime is { } endTime =>
                ResolveEndAt(endTime, options.EndAtDaysOfWeek, localNow, TimeZoneInfo.Local),
            _ => throw new SessionException("Locked and Strict sessions require a valid end condition.")
        };
    }

    private static void ValidateFocusCommitment(FocusCommitmentOptions options)
    {
        if (!Enum.IsDefined(options.Mode) || !Enum.IsDefined(options.EndCondition) || !Enum.IsDefined(options.AfterEnd))
        {
            throw new SessionException("Focus commitment contains an unsupported option.");
        }

        if (options.EndAtDaysOfWeek == null || options.EndAtDaysOfWeek.Any(day => !Enum.IsDefined(day)))
        {
            throw new SessionException("Fixed Time contains an invalid day of week.");
        }

        if (options.BreakCount is < 0 or > 5 ||
            options.BreakCount > 0 &&
            (options.BreakDuration < TimeSpan.FromMinutes(1) || options.BreakDuration > TimeSpan.FromMinutes(60)))
        {
            throw new SessionException("Break budget must be between 0 and 5 breaks of 1 to 60 minutes each.");
        }

        if (options.ScheduleLockMinutes is not (0 or 5 or 15 or 60))
        {
            throw new SessionException("Schedule configuration lock must be Off, 5 minutes, 15 minutes, or 1 hour.");
        }

        if (options is { AfterEnd: AfterEndBehavior.StartNextWorkspace, NextWorkspaceId: null })
        {
            throw new SessionException("Select the Workspace to start after this session.");
        }

        if (options.EndCondition == FocusEndCondition.Duration &&
            (options.Duration is not { } duration || duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(365)))
        {
            throw new SessionException("Session duration must be between 1 second and 1 year.");
        }
    }

    private string GetCommittedStateDirectory()
    {
        if (string.IsNullOrWhiteSpace(_committedSessionPath))
            throw new SessionException("Committed sessions cannot start because recovery storage is unavailable.");

        return Path.GetDirectoryName(_committedSessionPath) is { Length: > 0 } directory
            ? directory
            : throw new SessionException("Committed session recovery storage path is invalid.");
    }

    private void CheckCommittedStateStorage()
    {
        var directory = GetCommittedStateDirectory();
        var probe = Path.Combine(directory, $".commitment-preflight-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            using (File.Create(probe)) { }
        }
        catch (Exception ex)
        {
            throw new SessionException($"Committed session recovery storage is unavailable: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(probe)) File.Delete(probe);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not remove the temporary committed-session preflight file.");
            }
        }
    }

    private static DateTimeOffset ResolveEndAt(TimeOnly endTime, IReadOnlyCollection<DayOfWeek> daysOfWeek,
        DateTimeOffset localNow, TimeZoneInfo timeZone)
    {
        for (var daysAhead = 0; daysAhead <= 7; daysAhead++)
        {
            var date = localNow.Date.AddDays(daysAhead);
            if (daysOfWeek.Count > 0 && !daysOfWeek.Contains(date.DayOfWeek))
            {
                continue;
            }

            var localEnd = DateTime.SpecifyKind(date.Add(endTime.ToTimeSpan()), DateTimeKind.Unspecified);
            while (timeZone.IsInvalidTime(localEnd))
            {
                localEnd = localEnd.AddMinutes(1);
            }

            var offset = timeZone.IsAmbiguousTime(localEnd)
                ? timeZone.GetAmbiguousTimeOffsets(localEnd).Max()
                : timeZone.GetUtcOffset(localEnd);
            var end = new DateTimeOffset(localEnd, offset);
            if (end > localNow)
            {
                return end.ToUniversalTime();
            }
        }

        throw new SessionException("Fixed Time does not include a valid end day.");
    }

    public IModule? GetActiveModuleInstanceByInstanceId(Guid instanceId)
    {
        lock (_syncLock)
        {
            return _activeModules
                .FirstOrDefault(m => m.Configuration.InstanceId == instanceId)
                ?.Instance;
        }
    }

    public SessionSnapshot? GetCurrentSnapshot()
    {
        lock (_syncLock)
        {
            if (!IsSessionRunning || ActiveSession == null)
            {
                return null;
            }

            return new SessionSnapshot(
                ActiveSession.Id,
                ActiveSession.Name,
                _activeModules.Select(CreateModuleSnapshot).ToList());
        }
    }

    private static SessionModuleSnapshot CreateModuleSnapshot(ActiveModule active) => new(
        active.Configuration.InstanceId,
        active.Configuration.ModuleId,
        active.Definition.Name,
        active.Configuration.CustomName,
        active.Instance.GetSettings().Select(setting => new SessionSettingSnapshot(
            setting.Key,
            setting.GetCurrentLabel(),
            setting.Description,
            setting.ControlType,
            setting.Persistence,
            setting.GetCurrentReadOnly(),
            setting.GetCurrentVisibility(),
            setting.ValueType.Name,
            setting.GetValueAsString())).ToList(),
        active.Instance.GetActions().Select(action => new SessionActionSnapshot(
            action.Key,
            action.GetCurrentLabel(),
            action.GetCurrentEnabled())).ToList());

    private void TrackModuleFailed(ActiveModule module, string failureReason, long latencyMs)
    {
        var properties = ModuleTelemetryProperties(module);
        properties["result"] = "failed";
        properties["failureReason"] = failureReason;
        properties["latencyMs"] = latencyMs;
        telemetry.TrackEvent("ModuleStartFailed", properties);
    }

    private Dictionary<string, object?> ModuleTelemetryProperties(ActiveModule module) => new()
    {
        ["sessionInstanceId"] = _sessionInstanceId,
        ["presetId"] = ActiveSession?.Id,
        ["moduleId"] = module.Configuration.ModuleId,
        ["moduleName"] = module.Definition.Name,
        ["instanceId"] = module.Configuration.InstanceId
    };

    private void TrackSessionPreflightCompleted(Guid sessionInstanceId, Guid presetId, string startSource,
        string result, string? failureReason = null)
    {
        if (!telemetry.IsEnabled || !_reportedPreflightAttempts.TryAdd(sessionInstanceId, 0))
        {
            return;
        }

        var properties = new Dictionary<string, object?>
        {
            ["sessionInstanceId"] = sessionInstanceId,
            ["presetId"] = presetId,
            ["startSource"] = startSource,
            ["result"] = result
        };
        if (failureReason is not null)
        {
            properties["failureReason"] = failureReason;
        }

        telemetry.TrackEvent("SessionPreflightCompleted", properties);
    }

    private void TrackSessionStartFailed(Guid presetId, Guid sessionInstanceId, string startSource, Exception exception)
    {
        var stage = exception.Data.Contains(PreflightFailureDataKey) ? "preflight" : "session_initialization";
        telemetry.TrackEvent("SessionStartFailed", new Dictionary<string, object?>
        {
            ["sessionInstanceId"] = sessionInstanceId,
            ["presetId"] = presetId,
            ["startSource"] = startSource,
            ["stage"] = stage,
            ["failureReason"] = ProductAnalyticsProperties.FailureReason(exception),
            ["result"] = "failed"
        });
    }

    private void TrackSessionStarted(SessionPreset preset, List<ActiveModule> modules)
    {
        if (!telemetry.IsEnabled) return;

        var properties = SessionTelemetryProperties(
            preset, modules, _sessionInstanceId, _sessionStartSource, _sessionScheduleId,
            _previousSessionInstanceId);
        properties["protectionState"] = GetProtectionState(ProtectionStatus);
        telemetry.TrackEvent("SessionStarted", properties);
    }

    private void TrackSessionStopped(SessionPreset preset, List<ActiveModule> modules, DateTimeOffset? startedAt,
        Guid? sessionInstanceId, string startSource, SessionEndReason reason, int breaksUsed,
        bool emergencyUnlockUsed, bool protectionDegraded, Guid? scheduleId,
        Guid? previousSessionInstanceId)
    {
        if (!telemetry.IsEnabled) return;

        var properties = SessionTelemetryProperties(preset, modules, sessionInstanceId, startSource, scheduleId,
            previousSessionInstanceId);
        properties["stopReason"] = reason switch
        {
            SessionEndReason.NaturalCompletion => "natural_completion",
            SessionEndReason.EmergencyUnlock => "emergency_unlock",
            SessionEndReason.StartupFailure => "startup_failure",
            _ => "user_stop"
        };
        properties["completedAsPlanned"] = reason == SessionEndReason.NaturalCompletion;
        properties["durationMs"] = startedAt.HasValue
            ? (long)(DateTimeOffset.UtcNow - startedAt.Value).TotalMilliseconds
            : null;
        properties["breaksUsed"] = breaksUsed;
        properties["emergencyUnlockUsed"] = emergencyUnlockUsed;
        properties["protectionDegraded"] = protectionDegraded;
        telemetry.TrackEvent("SessionStopped", properties);
    }

    private Dictionary<string, object?> SessionTelemetryProperties(SessionPreset preset, List<ActiveModule> modules,
        Guid? sessionInstanceId, string startSource, Guid? scheduleId, Guid? previousSessionInstanceId)
    {
        var moduleSummaries = modules.Select(module => ProductAnalyticsProperties.Module(
            module.Configuration, module.Definition.Name,
            ProductAnalyticsProperties.HasHomeAssistantAccessToken(module.Configuration,
                module.Definition.Name, _secureStorage), _secureStorage)).ToArray();
        var properties = new Dictionary<string, object?>
        {
            ["sessionInstanceId"] = sessionInstanceId,
            ["presetId"] = preset.Id,
            ["startSource"] = startSource,
            ["moduleCount"] = modules.Count,
            ["modules"] = moduleSummaries,
            ["moduleIds"] = modules.Select(module => module.Configuration.ModuleId.ToString()).ToArray(),
            ["moduleTypes"] = moduleSummaries.Select(module => (string)module["moduleName"]!).Distinct().ToArray()
        };
        foreach (var (key, value) in ProductAnalyticsProperties.FocusCommitment(preset.FocusCommitment))
        {
            properties[key] = value;
        }
        if (scheduleId.HasValue) properties["scheduleId"] = scheduleId.Value;
        if (previousSessionInstanceId.HasValue)
            properties["previousSessionInstanceId"] = previousSessionInstanceId.Value;
        return properties;
    }


    private static string GetProtectionState(string status) => status switch
    {
        var value when value.StartsWith("Protection degraded", StringComparison.OrdinalIgnoreCase) => "degraded",
        var value when value.StartsWith("Protection recovered", StringComparison.OrdinalIgnoreCase) => "recovered",
        var value when value.StartsWith("Protection failed", StringComparison.OrdinalIgnoreCase) => "failed",
        var value when value.StartsWith("Protection active", StringComparison.OrdinalIgnoreCase) => "active",
        _ => "inactive"
    };

    public async ValueTask DisposeAsync()
    {
        if (IsSessionRunning && ActiveSession?.FocusCommitment.Mode == FocusCommitmentMode.Normal)
        {
            await StopCurrentSessionAsync().ConfigureAwait(false);
        }

        _sessionCts?.Dispose();
        _asyncLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
