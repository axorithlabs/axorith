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
public class SessionManager : ISessionManager
{
    private readonly IModuleRegistry _moduleRegistry;
    private readonly ILogger<SessionManager> _logger;
    private readonly TimeSpan _validationTimeout;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _shutdownTimeout;

    public SessionManager(
        IModuleRegistry moduleRegistry, 
        ILogger<SessionManager> logger,
        TimeSpan validationTimeout,
        TimeSpan startupTimeout,
        TimeSpan shutdownTimeout)
    {
        _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validationTimeout = validationTimeout;
        _startupTimeout = startupTimeout;
        _shutdownTimeout = shutdownTimeout;
    }

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
    public Task StartSessionAsync(SessionPreset preset, CancellationToken cancellationToken = default)
    {
        if (IsSessionRunning)
            throw new SessionException(
                "A session is already running. Stop the current session before starting a new one.");

        _logger.LogInformation("Starting session '{PresetName}'...", preset.Name);
        ActiveSession = preset;
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Immediately notify listeners that the session has started so the UI can update.
        SessionStarted?.Invoke(preset.Id);

        _activeModules.Clear();

        foreach (var configuredModule in preset.Modules)
        {
            var (instance, scope) = _moduleRegistry.CreateInstance(configuredModule.ModuleId);
            if (instance != null && scope != null)
                _activeModules.Add(new ActiveModule
                    { Instance = instance, Scope = scope, Configuration = configuredModule });
            else
                _logger.LogWarning(
                    "Failed to create instance for module with ID {ModuleId} in preset '{PresetName}'. Skipping",
                    configuredModule.ModuleId, preset.Name);
        }

        if (_activeModules.Count == 0)
        {
            _logger.LogWarning("No modules could be instantiated for preset '{PresetName}'. Aborting session start",
                preset.Name);
            // We need to roll back the "started" state
            ActiveSession = null;
            SessionStopped?.Invoke(preset.Id);
            return Task.CompletedTask;
        }

        // Run module startup in the background without blocking the caller.
        _ = RunModuleStartupsAsync(_activeModules, _sessionCts.Token);

        return Task.CompletedTask;
    }

    private async Task RunModuleStartupsAsync(IReadOnlyCollection<ActiveModule> modules,
        CancellationToken cancellationToken)
    {
        var startTasks = modules.Select(async activeModule =>
        {
            var config = activeModule.Configuration;
            var definition = activeModule.Scope.Resolve<ModuleDefinition>();

            var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["SessionId"] = ActiveSession?.Id ?? Guid.Empty,
                ["ModuleName"] = definition.Name,
                ["ModuleInstanceName"] = config.CustomName ?? string.Empty
            });

            try
            {
                // Populate the module's reactive settings with values from the preset
                var moduleSettings = activeModule.Instance.GetSettings().ToDictionary(s => s.Key);
                foreach (var savedSetting in activeModule.Configuration.Settings)
                    if (moduleSettings.TryGetValue(savedSetting.Key, out var setting))
                        setting.SetValueFromString(savedSetting.Value);

                // Validate settings with configured timeout
                using var validationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                validationCts.CancelAfter(_validationTimeout);

                var validation = await activeModule.Instance.ValidateSettingsAsync(validationCts.Token)
                    .ConfigureAwait(false);
                if (validation.Status != ValidationStatus.Ok)
                {
                    _logger.LogError("Module validation failed: {Error}", validation.Message);
                    throw new SessionException($"Module validation failed: {validation.Message}");
                }

                // Start the module with configured timeout
                using var startCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startCts.CancelAfter(_startupTimeout);

                await activeModule.Instance.OnSessionStartAsync(startCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Module {InstanceName} startup timed out after {Timeout}s", 
                    config.CustomName ?? definition.Name, _startupTimeout.TotalSeconds);
                throw new SessionException($"Module startup timed out after {_startupTimeout.TotalSeconds} seconds");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Module {InstanceName} startup was canceled", config.CustomName ?? definition.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {InstanceName} failed to start", config.CustomName ?? definition.Name);
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
                _logger.LogInformation("All {Count} modules for session '{PresetName}' started successfully",
                    modules.Count, ActiveSession.Name);
        }
        catch
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (ActiveSession is not null)
                    _logger.LogInformation("Session '{PresetName}' startup was canceled by user", ActiveSession.Name);
            }
            else
            {
                if (ActiveSession is not null)
                    _logger.LogCritical(
                        "One or more modules failed to start for session '{PresetName}'. Attempting to roll back...",
                        ActiveSession.Name);

                await StopCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task StopCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSessionRunning)
        {
            _logger.LogWarning("No session is currently running");
            return;
        }

        _logger.LogInformation("Stopping current session...");

        try
        {
            // Cancel session CancellationToken
            _sessionCts?.Cancel();

            // Stop all active modules with configured shutdown timeout
            foreach (var activeModule in _activeModules)
            {
                _logger.LogInformation("Stopping module...");

                try
                {
                    using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    shutdownCts.CancelAfter(_shutdownTimeout);
                    
                    await activeModule.Instance.OnSessionEndAsync(shutdownCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Module OnSessionEndAsync timed out after {Timeout}s - forcing disposal", 
                        _shutdownTimeout.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Module threw an exception during OnSessionEndAsync");
                }
            }

            // Dispose all modules and scopes
            foreach (var activeModule in _activeModules)
                try
                {
                    activeModule.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while disposing a module or its scope");
                }

            _activeModules.Clear();
            _sessionCts?.Dispose();
            _sessionCts = null;
            ActiveSession = null;

            _logger.LogInformation("Session stopped successfully");
            SessionStopped?.Invoke(ActiveSession?.Id ?? Guid.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session stop");
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