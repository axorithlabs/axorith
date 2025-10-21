using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Shared.Exceptions;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

/// <summary>
/// The concrete implementation for managing the session lifecycle.
/// This class orchestrates the startup and shutdown of modules based on a preset.
/// </summary>
public class SessionManager(IModuleRegistry moduleRegistry, ILogger<SessionManager> logger) : ISessionManager
{
    private SessionPreset? _activeSession;
    private CancellationTokenSource? _sessionCts;
    private readonly List<IModule> _activeModules = new();

    public bool IsSessionRunning => _activeSession != null;

    public event Action? SessionStarted;
    public event Action? SessionStopped;
    
    public async Task StartSessionAsync(SessionPreset preset)
    {
        if (IsSessionRunning)
        {
            throw new SessionException("A session is already running. Stop the current session before starting a new one.");
        }

        logger.LogInformation("Starting session '{PresetName}'...", preset.Name);
        _activeSession = preset;
        _sessionCts = new CancellationTokenSource();
        _activeModules.Clear();

        var modulesToStart = new List<IModule>();
        foreach (var configuredModule in preset.Modules)
        {
            var moduleInstance = moduleRegistry.CreateInstance(configuredModule.ModuleId);
            if (moduleInstance == null)
            {
                logger.LogWarning("Module with ID {ModuleId} not found in registry. Skipping.", configuredModule.ModuleId);
                continue;
            }
            modulesToStart.Add(moduleInstance);
        }

        // Design decision: The session will attempt to start even if some modules fail.
        // This allows for partial functionality. The errors are logged, and the user
        // can be notified at a higher level if necessary.
        try
        {
            var startTasks = modulesToStart.Select(async module =>
            {
                try
                {
                    var settings = preset.Modules.First(cm => cm.ModuleId == module.Id).Settings;
                    await module.OnSessionStartAsync(settings, _sessionCts.Token);
                    
                    // Only add to active modules on successful start
                    lock (_activeModules)
                    {
                        _activeModules.Add(module);
                    }
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning("Start of module '{ModuleName}' was cancelled.", module.Name);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Module '{ModuleName}' failed to start. It will be excluded from this session.", module.Name);
                }
            });

            await Task.WhenAll(startTasks);

            if (_activeModules.Count == 0 && modulesToStart.Count > 0)
            {
                logger.LogError("All modules failed to start for session '{PresetName}'. Stopping session immediately.", preset.Name);
                await StopCurrentSessionAsync();
                return;
            }

            logger.LogInformation("Session '{PresetName}' started successfully with {Count}/{Total} modules.", preset.Name, _activeModules.Count, modulesToStart.Count);
            SessionStarted?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "An unexpected critical error occurred while starting the session. Attempting to roll back...");
            await StopCurrentSessionAsync();
            throw;
        }
    }

    public async Task StopCurrentSessionAsync()
    {
        if (!IsSessionRunning)
        {
            return;
        }

        var sessionName = _activeSession!.Name;
        logger.LogInformation("Stopping session '{PresetName}'...", sessionName);

        if (_sessionCts != null)
            await _sessionCts.CancelAsync();

        var modulesToStop = new List<IModule>(_activeModules);

        var stopTasks = modulesToStop.Select(async module =>
        {
            try
            {
                await module.OnSessionEndAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Module '{ModuleName}' failed during shutdown. Continuing with others.", module.Name);
            }
        });

        await Task.WhenAll(stopTasks);
        
        foreach (var module in _activeModules)
        {
            try
            {
                module.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Module '{ModuleName}' threw an exception during Dispose.", module.Name);
            }
        }

        _activeSession = null;
        _activeModules.Clear();
        _sessionCts?.Dispose();
        _sessionCts = null;

        logger.LogInformation("Session '{PresetName}' stopped.", sessionName);
        SessionStopped?.Invoke();
    }
}