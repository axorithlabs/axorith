using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Shared.Exceptions;
using Microsoft.Extensions.Logging;

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
    TimeSpan shutdownTimeout)
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
        List<ActiveModule> modulesToStart;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (IsSessionRunning)
            {
                throw new SessionException(
                    "A session is already running. Stop the current session before starting a new one.");
            }

            logger.LogInformation("Starting session '{PresetName}'...", preset.Name);
            
            ActiveSession = preset;
            SessionStartedAt = DateTimeOffset.UtcNow;
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeModules.Clear();

            foreach (var configuredModule in preset.Modules)
            {
                var (instance, scope) = moduleRegistry.CreateInstance(configuredModule.ModuleId);
                if (instance != null && scope != null)
                {
                    _activeModules.Add(new ActiveModule
                    {
                        Instance = instance, Scope = scope, Configuration = configuredModule
                    });
                }
                else
                {
                    logger.LogWarning(
                        "Failed to create instance for module with ID {ModuleId} in preset '{PresetName}'. Skipping",
                        configuredModule.ModuleId, preset.Name);
                }
            }

            if (_activeModules.Count == 0)
            {
                ActiveSession = null;
                SessionStartedAt = null;
                _sessionCts.Dispose();
                _sessionCts = null;
                
                logger.LogWarning("No modules could be instantiated for preset '{PresetName}'. Aborting session start",
                    preset.Name);
                throw new SessionException(
                    $"No modules could be instantiated for preset '{preset.Name}'. Aborting session start");
            }

            // Create a snapshot of the list for the startup process.
            // This prevents "Collection was modified" exception if Stop() is called 
            // and clears _activeModules while startup is iterating.
            modulesToStart = _activeModules.ToList();
        }
        finally
        {
            _stateLock.Release();
        }

        try
        {
            await RunModuleStartupsSequentialAsync(modulesToStart, _sessionCts.Token).ConfigureAwait(false);
            SessionStarted?.Invoke(preset.Id);
        }
        catch (Exception)
        {
            // If startup fails, we must ensure we stop cleanly.
            // We call StopCurrentSessionAsync which will acquire the lock and clean up.
            await StopCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RunModuleStartupsSequentialAsync(IReadOnlyList<ActiveModule> modules,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting {Count} modules sequentially...", modules.Count);

        foreach (var activeModule in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var config = activeModule.Configuration;
            var definition = activeModule.Scope.Resolve<ModuleDefinition>();
            var moduleName = config.CustomName ?? definition.Name;

            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["SessionId"] = ActiveSession?.Id ?? Guid.Empty,
                ["ModuleName"] = definition.Name,
                ["ModuleInstanceName"] = moduleName
            });

            try
            {
                var moduleSettings = activeModule.Instance.GetSettings().ToDictionary(s => s.Key);
                foreach (var savedSetting in activeModule.Configuration.Settings)
                {
                    if (moduleSettings.TryGetValue(savedSetting.Key, out var setting))
                    {
                        setting.SetValueFromString(savedSetting.Value);
                    }
                }

                using (var validationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    validationCts.CancelAfter(validationTimeout);
                    var validation = await activeModule.Instance.ValidateSettingsAsync(validationCts.Token)
                        .ConfigureAwait(false);
                    if (validation.Status != ValidationStatus.Ok)
                    {
                        logger.LogError("Module validation failed: {Error}", validation.Message);
                        throw new SessionException($"Module '{moduleName}' validation failed: {validation.Message}");
                    }
                }

                if (config.StartDelay > TimeSpan.Zero)
                {
                    logger.LogInformation("Waiting {Delay}s before starting module '{InstanceName}'...",
                        config.StartDelay.TotalSeconds, moduleName);

                    try
                    {
                        await Task.Delay(config.StartDelay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogWarning("Session startup cancelled during delay for module '{InstanceName}'",
                            moduleName);
                        throw;
                    }
                }

                logger.LogInformation("Starting module '{InstanceName}'...", moduleName);
                using (var startCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    startCts.CancelAfter(startupTimeout);
                    await activeModule.Instance.OnSessionStartAsync(startCts.Token).ConfigureAwait(false);
                }

                logger.LogInformation("Module '{InstanceName}' started successfully", moduleName);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Module {InstanceName} startup timed out after {Timeout}s",
                    moduleName, startupTimeout.TotalSeconds);
                throw new SessionException($"Module '{moduleName}' startup timed out");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Module {InstanceName} failed to start", moduleName);
                throw new SessionException($"Module '{moduleName}' failed to start: {ex.Message}");
            }
        }

        logger.LogInformation("All modules started successfully");
    }

    /// <inheritdoc />
    public async Task StopCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsSessionRunning && _activeModules.Count == 0)
            {
                logger.LogWarning("No session is currently running");
                return;
            }

            logger.LogInformation("Stopping current session...");

            // Signal cancellation to any running startup tasks
            _sessionCts?.Cancel();

            for (var i = _activeModules.Count - 1; i >= 0; i--)
            {
                var activeModule = _activeModules[i];
                var definition = activeModule.Scope.Resolve<ModuleDefinition>();
                var moduleName = activeModule.Configuration.CustomName ?? definition.Name;

                logger.LogInformation("Stopping module '{InstanceName}'...", moduleName);

                try
                {
                    using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    shutdownCts.CancelAfter(shutdownTimeout);

                    await activeModule.Instance.OnSessionEndAsync(shutdownCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Module '{InstanceName}' stop timed out - forcing disposal", moduleName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Module '{InstanceName}' threw an exception during stop", moduleName);
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
                    logger.LogError(ex, "An error occurred while disposing a module scope");
                }
            }

            var stoppedPresetId = ActiveSession?.Id ?? Guid.Empty;
            _activeModules.Clear();
            _sessionCts?.Dispose();
            _sessionCts = null;
            ActiveSession = null;
            SessionStartedAt = null;

            logger.LogInformation("Session stopped successfully");
            
            SessionStopped?.Invoke(stoppedPresetId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during session stop");
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
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
                var definition = moduleRegistry.GetDefinitionById(active.Configuration.ModuleId);

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
                    definition?.Name ?? "Unknown",
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
    }
}