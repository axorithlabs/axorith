using System.Collections.Concurrent;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Module.AppBlocker;

public class Module : IModule
{
    private readonly IModuleLogger _logger;
    private readonly IProcessBlocker _blocker;
    private readonly INotifier _notifier;
    private readonly Settings _settings;

    private readonly ConcurrentDictionary<string, DateTime> _lastNotificationTime = new();
    private readonly TimeSpan _notificationCooldown = TimeSpan.FromSeconds(10);

    public Module(IModuleLogger logger, IProcessBlocker blocker, INotifier notifier, IAppDiscoveryService appDiscovery)
    {
        _logger = logger;
        _blocker = blocker;
        _notifier = notifier;
        _settings = new Settings(); // Pass service

        _blocker.ProcessBlocked += OnProcessBlocked;
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _settings.GetSettings();
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _settings.GetActions();
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync();
    }

    public Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var processes = _settings.GetProcesses().ToList();

        _logger.LogInfo("Starting App Blocker with {Count} targets.", processes.Count);

        try
        {
            var killed = _blocker.Block(processes);

            if (killed.Count > 0)
            {
                var appList = string.Join(", ", killed);
                _notifier.ShowSystemAsync("Focus Mode: Distractions Cleared", $"Closed apps: {appList}");
                _logger.LogInfo("Initial cleanup closed: {Apps}", appList);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start blocking processes.");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task OnSessionEndAsync(CancellationToken cancellationToken)
    {
        _logger.LogInfo("Stopping App Blocker.");
        _blocker.UnblockAll();
        _lastNotificationTime.Clear();
        return Task.CompletedTask;
    }

    private void OnProcessBlocked(string processName)
    {
        var now = DateTime.UtcNow;

        if (_lastNotificationTime.TryGetValue(processName, out var lastTime))
        {
            if (now - lastTime < _notificationCooldown)
            {
                return;
            }
        }

        _lastNotificationTime[processName] = now;

        _ = _notifier.ShowSystemAsync("Focus Mode Active", $"Blocked distraction: {processName}");

        _logger.LogInfo("Blocked process '{ProcessName}' and notified user.", processName);
    }

    public void Dispose()
    {
        try
        {
            _blocker.ProcessBlocked -= OnProcessBlocked;
            _blocker.UnblockAll();
        }
        catch
        {
            // Ignore errors during dispose
        }

        GC.SuppressFinalize(this);
    }
}