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

    public event Action<Guid>? SessionStarted;
    public event Action<Guid>? SessionStopped;

    /// <inheritdoc />
    public async Task StartSessionAsync(SessionPreset preset, CancellationToken cancellationToken = default)
    {
        if (IsSessionRunning)
            throw new SessionException(
                "A session is already running. Stop the current session before starting a new one.");

        logger.LogInformation("Starting session '{PresetName}'...", preset.Name);
        ActiveSession = preset;
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _activeModules.Clear();

        foreach (var configuredModule in preset.Modules)
        {
            var (instance, scope) = moduleRegistry.CreateInstance(configuredModule.ModuleId);
            if (instance != null && scope != null)
                _activeModules.Add(new ActiveModule
                {
                    Instance = instance, Scope = scope, Configuration = configuredModule
                });
            else
                logger.LogWarning(
                    "Failed to create instance for module with ID {ModuleId} in preset '{PresetName}'. Skipping",
                    configuredModule.ModuleId, preset.Name);
        }

        if (_activeModules.Count == 0)
        {
            ActiveSession = null;
            logger.LogWarning("No modules could be instantiated for preset '{PresetName}'. Aborting session start",
                preset.Name);
            throw new SessionException($"No modules could be instantiated for preset '{preset.Name}'. Aborting session start");
        }

        // Await module startups and propagate failures to caller
        await RunModuleStartupsAsync(_activeModules, _sessionCts.Token).ConfigureAwait(false);

        // Notify listeners that the session has started only after successful module startups
        SessionStarted?.Invoke(preset.Id);
    }

    private async Task RunModuleStartupsAsync(IReadOnlyCollection<ActiveModule> modules,
        CancellationToken cancellationToken)
    {
        var startTasks = modules.Select(async activeModule =>
        {
            var config = activeModule.Configuration;
            var definition = activeModule.Scope.Resolve<ModuleDefinition>();

            var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["SessionId"] = ActiveSession?.Id ?? Guid.Empty,
                ["ModuleName"] = definition.Name,
                ["ModuleInstanceName"] = config.CustomName ?? string.Empty
            });

            try
            {
                var moduleSettings = activeModule.Instance.GetSettings().ToDictionary(s => s.Key);
                foreach (var savedSetting in activeModule.Configuration.Settings)
                    if (moduleSettings.TryGetValue(savedSetting.Key, out var setting))
                        setting.SetValueFromString(savedSetting.Value);

                using var validationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                validationCts.CancelAfter(validationTimeout);

                var validation = await activeModule.Instance.ValidateSettingsAsync(validationCts.Token)
                    .ConfigureAwait(false);
                if (validation.Status != ValidationStatus.Ok)
                {
                    logger.LogError("Module validation failed: {Error}", validation.Message);
                    throw new SessionException($"Module validation failed: {validation.Message}");
                }

                using var startCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startCts.CancelAfter(startupTimeout);

                await activeModule.Instance.OnSessionStartAsync(startCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Module {InstanceName} startup timed out after {Timeout}s",
                    config.CustomName ?? definition.Name, startupTimeout.TotalSeconds);
                throw new SessionException($"Module startup timed out after {startupTimeout.TotalSeconds} seconds");
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Module {InstanceName} startup was canceled", config.CustomName ?? definition.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Module {InstanceName} failed to start", config.CustomName ?? definition.Name);
                throw;
            }
            finally
            {
                scope?.Dispose();
            }
        });

        try
        {
            await Task.WhenAll(startTasks).ConfigureAwait(false);
            if (ActiveSession is not null)
                logger.LogInformation("All {Count} modules for session '{PresetName}' started successfully",
                    modules.Count, ActiveSession.Name);
        }
        catch (Exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (ActiveSession is not null)
                    logger.LogInformation("Session '{PresetName}' startup was canceled by user", ActiveSession.Name);

                await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                throw new SessionException("Session startup was canceled.");
            }

            if (ActiveSession is not null)
                logger.LogCritical(
                    "One or more modules failed to start for session '{PresetName}'. Attempting to roll back...",
                    ActiveSession.Name);

            await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            throw new SessionException($"One or more modules failed to start.");
        }
    }

    /// <inheritdoc />
    public async Task StopCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSessionRunning)
        {
            logger.LogWarning("No session is currently running");
            return;
        }

        logger.LogInformation("Stopping current session...");

        try
        {
            _sessionCts?.Cancel();

            foreach (var activeModule in _activeModules)
            {
                logger.LogInformation("Stopping module...");

                try
                {
                    using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    shutdownCts.CancelAfter(shutdownTimeout);

                    await activeModule.Instance.OnSessionEndAsync(shutdownCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Module OnSessionEndAsync timed out after {Timeout}s - forcing disposal",
                        shutdownTimeout.TotalSeconds);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Module threw an exception during OnSessionEndAsync");
                }
            }

            foreach (var activeModule in _activeModules)
                try
                {
                    activeModule.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred while disposing a module or its scope");
                }

            var stoppedPresetId = ActiveSession?.Id ?? Guid.Empty;
            _activeModules.Clear();
            _sessionCts?.Dispose();
            _sessionCts = null;
            ActiveSession = null;

            logger.LogInformation("Session stopped successfully");
            SessionStopped?.Invoke(stoppedPresetId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during session stop");
            throw;
        }
    }

    /// <summary>
    ///     Gets the active instance of a module by its module ID.
    ///     Returns null if the session is not running or the module is not active.
    /// </summary>
    /// <param name="moduleId">The unique identifier of the module definition.</param>
    /// <returns>The active module instance, or null if not found.</returns>
    public IModule? GetActiveModuleInstance(Guid moduleId)
    {
        return _activeModules
            .FirstOrDefault(m => m.Configuration.ModuleId == moduleId)
            ?.Instance;
    }

    /// <summary>
    ///     Gets the active instance of a module by its instance ID (from preset configuration).
    ///     Returns null if the session is not running or the module is not active.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the configured module instance.</param>
    /// <returns>The active module instance, or null if not found.</returns>
    public IModule? GetActiveModuleInstanceByInstanceId(Guid instanceId)
    {
        return _activeModules
            .FirstOrDefault(m => m.Configuration.InstanceId == instanceId)
            ?.Instance;
    }

    /// <summary>
    ///     Asynchronously disposes the SessionManager and ensures any active session is stopped cleanly.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (IsSessionRunning)
            await StopCurrentSessionAsync().ConfigureAwait(false);

        _sessionCts?.Dispose();
    }
}