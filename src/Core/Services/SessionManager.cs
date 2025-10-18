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
            var module = moduleRegistry.GetModuleById(configuredModule.ModuleId);
            if (module == null)
            {
                logger.LogWarning("Module with ID {ModuleId} not found in registry. Skipping.", configuredModule.ModuleId);
                continue;
            }
            modulesToStart.Add(module);
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
                    var context = new ModuleContextImplementation(module.Name, logger);
                    await module.OnSessionStartAsync(context, settings, _sessionCts.Token);
                    
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

        _sessionCts?.Cancel();

        var modulesToStop = new List<IModule>(_activeModules);

        var stopTasks = modulesToStop.Select(async module =>
        {
            try
            {
                var context = new ModuleContextImplementation(module.Name, logger);
                await module.OnSessionEndAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Module '{ModuleName}' failed during shutdown. Continuing with others.", module.Name);
            }
        });

        await Task.WhenAll(stopTasks);

        _activeSession = null;
        _activeModules.Clear();
        _sessionCts?.Dispose();
        _sessionCts = null;

        logger.LogInformation("Session '{PresetName}' stopped.", sessionName);
        SessionStopped?.Invoke();
    }

    /// <summary>
    /// A private, concrete implementation of the IModuleContext interface.
    /// This acts as a bridge between a module and the Core's logging system,
    /// ensuring that all module logs are prefixed with the module's name for easy identification.
    /// It is implemented as a private class as it is tightly coupled with the SessionManager's lifecycle.
    /// </summary>
    private class ModuleContextImplementation(string moduleName, ILogger logger) : IModuleContext
    {
        public void LogDebug(string messageTemplate, params object[] args) => logger.LogDebug("[{ModuleName}] " + messageTemplate, moduleName, args);
        public void LogInfo(string messageTemplate, params object[] args) => logger.LogInformation("[{ModuleName}] " + messageTemplate, moduleName, args);
        public void LogWarning(string messageTemplate, params object[] args) => logger.LogWarning("[{ModuleName}] " + messageTemplate, moduleName, args);
        public void LogError(Exception? exception, string messageTemplate, params object[] args) => logger.LogError(exception, "[{ModuleName}] " + messageTemplate, moduleName, args);
        public void LogFatal(Exception? exception, string messageTemplate, params object[] args) => logger.LogCritical(exception, "[{ModuleName}] " + messageTemplate, moduleName, args);
    }
}