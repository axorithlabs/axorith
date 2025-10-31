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
public class SessionManager(IModuleRegistry moduleRegistry, ILogger<SessionManager> logger)
    : ISessionManager, IDisposable
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

    private readonly List<ActiveModule> _activeModules = new();

    public bool IsSessionRunning => ActiveSession != null;
    public SessionPreset? ActiveSession { get; private set; }

    public event Action<Guid>? SessionStarted;
    public event Action<Guid>? SessionStopped;

    /// <inheritdoc />
    public Task StartSessionAsync(SessionPreset preset)
    {
        if (IsSessionRunning)
            throw new SessionException(
                "A session is already running. Stop the current session before starting a new one.");

        logger.LogInformation("Starting session '{PresetName}'...", preset.Name);
        ActiveSession = preset;
        _sessionCts = new CancellationTokenSource();

        // Immediately notify listeners that the session has started so the UI can update.
        SessionStarted?.Invoke(preset.Id);

        _activeModules.Clear();

        foreach (var configuredModule in preset.Modules)
        {
            var (instance, scope) = moduleRegistry.CreateInstance(configuredModule.ModuleId);
            if (instance != null && scope != null)
                _activeModules.Add(new ActiveModule
                    { Instance = instance, Scope = scope, Configuration = configuredModule });
            else
                logger.LogWarning(
                    "Failed to create instance for module with ID {ModuleId} in preset '{PresetName}'. Skipping",
                    configuredModule.ModuleId, preset.Name);
        }

        if (_activeModules.Count == 0)
        {
            logger.LogWarning("No modules could be instantiated for preset '{PresetName}'. Aborting session start",
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

            var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["ModuleName"] = definition.Name,
                ["ModuleInstanceName"] = config.CustomName ?? string.Empty
            });

            try
            {
                var finalSettings = activeModule.Instance.GetSettings()
                    .ToDictionary(def => def.Key, def => def.GetDefaultValueAsString());
                foreach (var savedSetting in config.Settings)
                    finalSettings[savedSetting.Key] = savedSetting.Value;

                await activeModule.Instance.OnSessionStartAsync(finalSettings, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Module instance {InstanceName} startup was canceled",
                    config.CustomName ?? definition.Name);
                // Don't rethrow cancellation exceptions
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Module instance {InstanceName} failed to start",
                    config.CustomName ?? definition.Name);
                // Rethrow to fail Task.WhenAll
                throw;
            }
            finally
            {
                scope?.Dispose();
            }
        });

        try
        {
            await Task.WhenAll(startTasks);
            if (ActiveSession is not null)
                logger.LogInformation("All {Count} modules for session '{PresetName}' started successfully",
                    modules.Count, ActiveSession.Name);
        }
        catch
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (ActiveSession is not null)
                    logger.LogInformation("Session '{PresetName}' startup was canceled by user", ActiveSession.Name);
            }
            else
            {
                if (ActiveSession is not null)
                    logger.LogCritical(
                        "One or more modules failed to start for session '{PresetName}'. Attempting to roll back...",
                        ActiveSession.Name);

                await StopCurrentSessionAsync();
            }
        }
    }

    /// <inheritdoc />
    public async Task StopCurrentSessionAsync()
    {
        if (!IsSessionRunning || ActiveSession is null) return;

        var sessionToStop = ActiveSession;
        logger.LogInformation("Stopping session '{PresetName}'...", sessionToStop.Name);

        _sessionCts?.Cancel();

        var stopTasks = _activeModules.Select(async activeModule =>
        {
            var config = activeModule.Configuration;
            var definition = activeModule.Scope.Resolve<ModuleDefinition>();

            var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["ModuleName"] = definition.Name,
                ["ModuleInstanceName"] = config.CustomName ?? string.Empty
            });

            try
            {
                await activeModule.Instance.OnSessionEndAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Module '{ModuleName}' threw an exception during OnSessionEndAsync.",
                    definition.Name);
            }
            finally
            {
                scope?.Dispose();
            }
        });
        await Task.WhenAll(stopTasks);

        foreach (var activeModule in _activeModules)
            try
            {
                activeModule.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while disposing a module or its scope");
            }

        _activeModules.Clear();
        _sessionCts?.Dispose();
        _sessionCts = null;
        ActiveSession = null;

        logger.LogInformation("Session '{PresetName}' stopped", sessionToStop.Name);
        SessionStopped?.Invoke(sessionToStop.Id);
    }

    /// <summary>
    ///     Disposes the SessionManager and ensures any active session is stopped cleanly.
    /// </summary>
    public void Dispose()
    {
        if (IsSessionRunning)
            // This is a blocking call, which is acceptable in Dispose.
            StopCurrentSessionAsync().GetAwaiter().GetResult();
    }
}