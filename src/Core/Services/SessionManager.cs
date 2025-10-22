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
    public async Task StartSessionAsync(SessionPreset preset)
    {
        if (IsSessionRunning)
            throw new SessionException(
                "A session is already running. Stop the current session before starting a new one.");

        logger.LogInformation("Starting session '{PresetName}'...", preset.Name);
        ActiveSession = preset;
        _sessionCts = new CancellationTokenSource();
        _activeModules.Clear();

        foreach (var configuredModule in preset.Modules)
        {
            var (instance, scope) = moduleRegistry.CreateInstance(configuredModule.ModuleId);
            if (instance != null && scope != null)
                _activeModules.Add(new ActiveModule { Instance = instance, Scope = scope });
            else
                logger.LogWarning(
                    "Failed to create instance for module with ID {ModuleId} in preset '{PresetName}'. Skipping",
                    configuredModule.ModuleId, preset.Name);
        }

        if (_activeModules.Count == 0)
        {
            logger.LogWarning("No modules could be instantiated for preset '{PresetName}'. Aborting session start",
                preset.Name);
            ActiveSession = null;
            return;
        }

        try
        {
            var startTasks = _activeModules.Select(activeModule =>
            {
                var definition = activeModule.Scope.Resolve<ModuleDefinition>();
                var configuredModule = preset.Modules.First(cm => cm.ModuleId == definition.Id);
                return activeModule.Instance.OnSessionStartAsync(configuredModule.Settings, _sessionCts.Token);
            });

            await Task.WhenAll(startTasks);

            logger.LogInformation("Session '{PresetName}' started successfully with {Count} modules", preset.Name,
                _activeModules.Count);
            SessionStarted?.Invoke(preset.Id);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "A critical error occurred while starting modules for session '{PresetName}'. Attempting to roll back...",
                preset.Name);
            await StopCurrentSessionAsync();
            throw;
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
            try
            {
                await activeModule.Instance.OnSessionEndAsync();
            }
            catch (Exception ex)
            {
                var def = activeModule.Scope.Resolve<ModuleDefinition>();
                logger.LogError(ex, "Module '{ModuleName}' threw an exception during OnSessionEndAsync", def.Name);
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