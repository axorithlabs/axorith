using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Shared.Exceptions;
using Microsoft.Extensions.Logging;
using Axorith.Telemetry;

namespace Axorith.Core.Services;

/// <summary>
///     The concrete implementation for managing the session lifecycle.
///     This class orchestrates the startup and shutdown of modules based on a preset,
///     managing their isolated lifetime scopes.
/// </summary>
public class SessionManager(
    IModuleRegistry moduleRegistry,
    ILogger<SessionManager> logger,
    TimeSpan validationTimeout,
    TimeSpan startupTimeout,
    TimeSpan shutdownTimeout,
    ITelemetryService telemetry)
    : ISessionManager
{
    private CancellationTokenSource? _sessionCts;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // This private class holds the live instance of a module and its personal DI scope.
    private class ActiveModule : IDisposable
    {
        public required IModule Instance { get; init; }
        public required ILifetimeScope Scope { get; init; }
        public required ConfiguredModule Configuration { get; init; }
        public required ModuleDefinition Definition { get; init; }

        public string DisplayName => Configuration.CustomName ?? Definition.Name;

        public void Dispose()
        {
            Instance.Dispose();
            Scope.Dispose();
        }
    }

    private readonly List<ActiveModule> _activeModules = [];

    public bool IsSessionRunning => ActiveSession != null;
    public SessionPreset? ActiveSession { get; private set; }
    public DateTimeOffset? SessionStartedAt { get; private set; }

    public event Action<Guid>? SessionStarted;
    public event Action<Guid>? SessionStopped;

    /// <inheritdoc />
    public async Task StartSessionAsync(SessionPreset preset, CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (IsSessionRunning)
            {
                throw new SessionException(
                    "A session is already running. Stop the current session before starting a new one.");
            }

            logger.LogInformation("Initializing session '{PresetName}'...", preset.Name);

            ActiveSession = preset;
            SessionStartedAt = DateTimeOffset.UtcNow;
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeModules.Clear();

            foreach (var configuredModule in preset.Modules)
            {
                var (instance, scope) = moduleRegistry.CreateInstance(configuredModule.ModuleId);
                
                if (instance != null && scope != null)
                {
                    var definition = scope.Resolve<ModuleDefinition>();
                    var activeModule = new ActiveModule
                    {
                        Instance = instance,
                        Scope = scope,
                        Configuration = configuredModule,
                        Definition = definition
                    };

                    ApplySettings(activeModule);
                    _activeModules.Add(activeModule);
                }
                else
                {
                    logger.LogWarning(
                        "Failed to create instance for module {ModuleId} in preset '{PresetName}'. Skipping.",
                        configuredModule.ModuleId, preset.Name);
                }
            }

            if (_activeModules.Count == 0)
            {
                CleanupSessionState();
                throw new SessionException($"No modules could be instantiated for preset '{preset.Name}'. Aborting.");
            }
        }
        finally
        {
            _stateLock.Release();
        }

        try
        {
            await ValidateAllModulesAsync(_activeModules, _sessionCts.Token).ConfigureAwait(false);

            await RunHybridStartupAsync(_activeModules, _sessionCts.Token).ConfigureAwait(false);

            logger.LogInformation("Session '{PresetName}' started successfully with {Count} modules.",
                preset.Name, _activeModules.Count);
            SessionStarted?.Invoke(preset.Id);
            TrackSessionStarted(preset, _activeModules);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Session startup failed. Initiating rollback...");
            await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
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

                // Handle the delayed module
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
            TrackModuleStarted(module);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out specifically
            logger.LogError("Module '{InstanceName}' startup timed out after {Timeout}s.", 
                module.DisplayName, startupTimeout.TotalSeconds);
            throw new SessionException($"Module '{module.DisplayName}' startup timed out.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Module '{InstanceName}' failed to start.", module.DisplayName);
            throw new SessionException($"Module '{module.DisplayName}' failed to start: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task StopCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsSessionRunning && _activeModules.Count == 0)
            {
                logger.LogWarning("No session is currently running.");
                return;
            }

            logger.LogInformation("Stopping current session...");

            // Signal cancellation to any running startup tasks immediately
            _sessionCts?.Cancel();

            // Stop modules in reverse order (LIFO)
            // We do this sequentially to ensure dependencies (if any implicit ones exist) are torn down correctly.
            // Parallel stopping is risky as one module might depend on another being alive during shutdown.
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
                    logger.LogError(ex, "Module '{InstanceName}' threw an exception during stop.", activeModule.DisplayName);
                }
            }

            // Dispose all scopes
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

            var stoppedPresetId = ActiveSession?.Id ?? Guid.Empty;
            var stoppedPresetName = ActiveSession?.Name;
            var stoppedModules = _activeModules.ToList();
            var startedAt = SessionStartedAt;
            
            CleanupSessionState();

            logger.LogInformation("Session stopped successfully.");
            
            if (stoppedPresetId != Guid.Empty)
            {
                SessionStopped?.Invoke(stoppedPresetId);
                TrackSessionStopped(stoppedPresetId, stoppedPresetName, stoppedModules, startedAt);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Critical error during session stop.");
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void CleanupSessionState()
    {
        _activeModules.Clear();
        _sessionCts?.Dispose();
        _sessionCts = null;
        ActiveSession = null;
        SessionStartedAt = null;
    }

    /// <inheritdoc />
    public IModule? GetActiveModuleInstance(Guid moduleId)
    {
        _stateLock.Wait();
        try
        {
            return _activeModules
                .FirstOrDefault(m => m.Configuration.ModuleId == moduleId)
                ?.Instance;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public IModule? GetActiveModuleInstanceByInstanceId(Guid instanceId)
    {
        _stateLock.Wait();
        try
        {
            return _activeModules
                .FirstOrDefault(m => m.Configuration.InstanceId == instanceId)
                ?.Instance;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public SessionSnapshot? GetCurrentSnapshot()
    {
        _stateLock.Wait();
        try
        {
            if (!IsSessionRunning || ActiveSession == null)
            {
                return null;
            }

            var modules = new List<SessionModuleSnapshot>();

            foreach (var active in _activeModules)
            {
                var settings = new List<SessionSettingSnapshot>();
                foreach (var setting in active.Instance.GetSettings())
                {
                    settings.Add(new SessionSettingSnapshot(
                        setting.Key,
                        setting.GetCurrentLabel(),
                        setting.Description,
                        setting.ControlType,
                        setting.Persistence,
                        setting.GetCurrentReadOnly(),
                        setting.GetCurrentVisibility(),
                        setting.ValueType.Name,
                        setting.GetValueAsString()));
                }

                var actions = new List<SessionActionSnapshot>();
                foreach (var action in active.Instance.GetActions())
                {
                    actions.Add(new SessionActionSnapshot(
                        action.Key,
                        action.GetCurrentLabel(),
                        action.GetCurrentEnabled()));
                }

                modules.Add(new SessionModuleSnapshot(
                    active.Configuration.InstanceId,
                    active.Configuration.ModuleId,
                    active.Definition.Name,
                    active.Configuration.CustomName,
                    settings,
                    actions));
            }

            return new SessionSnapshot(ActiveSession.Id, ActiveSession.Name, modules);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public SessionModuleSnapshot? GetModuleSnapshotByInstanceId(Guid instanceId)
    {
        // GetCurrentSnapshot already takes the lock
        var snapshot = GetCurrentSnapshot();
        return snapshot?.Modules.FirstOrDefault(m => m.InstanceId == instanceId);
    }

    private void TrackModuleStarted(ActiveModule module)
    {
        if (!telemetry.IsEnabled)
        {
            return;
        }

        var settingsPreview = module.Configuration.Settings
            .Take(32)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    len = kvp.Value?.Length ?? 0,
                    val = TelemetryGuard.SafeString(kvp.Value, 128)
                });

        telemetry.TrackEvent("ModuleUsed", new Dictionary<string, object?>
        {
            ["moduleId"] = module.Configuration.ModuleId.ToString(),
            ["moduleName"] = module.Definition.Name,
            ["instanceId"] = module.Configuration.InstanceId.ToString(),
            ["presetId"] = ActiveSession?.Id.ToString(),
            ["presetName"] = TelemetryGuard.SafeString(ActiveSession?.Name, 128),
            ["startDelaySec"] = (int)module.Configuration.StartDelay.TotalSeconds,
            ["customName"] = !string.IsNullOrWhiteSpace(module.Configuration.CustomName),
            ["settingsCount"] = module.Configuration.Settings.Count,
            ["settings"] = settingsPreview
        });
    }

    private void TrackSessionStarted(SessionPreset preset, List<ActiveModule> modules)
    {
        if (!telemetry.IsEnabled)
        {
            return;
        }

        var moduleSummaries = modules.Select(m => new
        {
            moduleId = m.Configuration.ModuleId.ToString(),
            moduleName = TelemetryGuard.SafeString(m.Definition.Name),
            instanceId = m.Configuration.InstanceId.ToString(),
            startDelayMs = (long)m.Configuration.StartDelay.TotalMilliseconds,
            settingsKeys = m.Configuration.Settings.Keys.Take(32).ToArray(),
            customName = TelemetryGuard.SafeString(m.Configuration.CustomName),
            settingsCount = m.Configuration.Settings.Count
        }).ToArray();

        var settingsKeysDistinct = modules
            .SelectMany(m => m.Configuration.Settings.Keys)
            .Distinct()
            .Take(128)
            .ToArray();

        telemetry.TrackEvent("HostSessionStarted", new Dictionary<string, object?>
        {
            ["presetId"] = preset.Id.ToString(),
            ["presetName"] = TelemetryGuard.SafeString(preset.Name, 128),
            ["presetNameLength"] = preset.Name?.Length ?? 0,
            ["moduleCount"] = modules.Count,
            ["modules"] = moduleSummaries,
            ["settingsKeyCount"] = settingsKeysDistinct.Length,
            ["settingsKeys"] = settingsKeysDistinct
        });
    }

    private void TrackSessionStopped(Guid presetId, string? presetName, List<ActiveModule> modules, DateTimeOffset? startedAt)
    {
        if (!telemetry.IsEnabled)
        {
            return;
        }

        var durationMs = startedAt.HasValue
            ? (long)(DateTimeOffset.UtcNow - startedAt.Value).TotalMilliseconds
            : (long?)null;

        var moduleSummaries = modules.Select(m => new
        {
            moduleId = m.Configuration.ModuleId.ToString(),
            moduleName = TelemetryGuard.SafeString(m.Definition.Name),
            instanceId = m.Configuration.InstanceId.ToString(),
            startDelayMs = (long)m.Configuration.StartDelay.TotalMilliseconds,
            settingsKeys = m.Configuration.Settings.Keys.Take(32).ToArray(),
            customName = TelemetryGuard.SafeString(m.Configuration.CustomName),
            settingsCount = m.Configuration.Settings.Count
        }).ToArray();

        var settingsKeysDistinct = modules
            .SelectMany(m => m.Configuration.Settings.Keys)
            .Distinct()
            .Take(128)
            .ToArray();

        telemetry.TrackEvent("HostSessionStopped", new Dictionary<string, object?>
        {
            ["presetId"] = presetId.ToString(),
            ["presetName"] = TelemetryGuard.SafeString(presetName, 128),
            ["presetNameLength"] = presetName?.Length ?? 0,
            ["moduleCount"] = modules.Count,
            ["modules"] = moduleSummaries,
            ["settingsKeyCount"] = settingsKeysDistinct.Length,
            ["settingsKeys"] = settingsKeysDistinct,
            ["durationMs"] = durationMs
        });
    }

    /// <summary>
    ///     Asynchronously disposes the SessionManager and ensures any active session is stopped cleanly.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (IsSessionRunning)
        {
            await StopCurrentSessionAsync().ConfigureAwait(false);
        }

        _sessionCts?.Dispose();
        _stateLock.Dispose();
        GC.SuppressFinalize(this);
    }
}