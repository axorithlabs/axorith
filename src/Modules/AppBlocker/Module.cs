using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Module.AppBlocker;

public class Module(IModuleLogger logger, IProcessBlocker blocker) : IModule
{
    private readonly Settings _settings = new();

    public IReadOnlyList<ISetting> GetSettings() => _settings.GetSettings();
    public IReadOnlyList<IAction> GetActions() => _settings.GetActions();

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync();
    }

    public Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var processes = _settings.GetProcesses().ToList();

        logger.LogInfo("Starting App Blocker with {Count} targets.", processes.Count);

        try
        {
            blocker.Block(processes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start blocking processes.");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task OnSessionEndAsync(CancellationToken cancellationToken)
    {
        logger.LogInfo("Stopping App Blocker.");
        blocker.UnblockAll();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try 
        {
            blocker.UnblockAll();
        }
        catch
        {
            // Ignore errors during dispose
        }
        
        GC.SuppressFinalize(this);
    }
}