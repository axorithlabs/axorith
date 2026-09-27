using System.Collections.Concurrent;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Axorith.Shared.Platform;

namespace Axorith.Module.AppBlocker;

public class Module : IModule, ISessionBreakParticipant, ICommittedSessionValidator,
    IWorkspaceApplicationAllowlist
{
    private readonly IModuleLogger _logger;
    private readonly IProcessBlocker _blocker;
    private readonly INotifier _notifier;
    private readonly Settings _settings;
    private bool _isBlocking;
    private bool _pausedForBreak;
    private string[] _workspaceApplications = [];
    private bool _workspaceApplicationsReceived;

    private readonly ConcurrentDictionary<string, DateTime> _lastNotificationTime = new();
    private readonly TimeSpan _notificationCooldown = TimeSpan.FromSeconds(10);

    public Module(IModuleLogger logger, IProcessBlocker blocker, INotifier notifier, IAppDiscoveryService appDiscovery)
    {
        _logger = logger;
        _blocker = blocker;
        _notifier = notifier;
        _settings = new Settings(appDiscovery);

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
        if (_settings.IsAllowList)
        {
            if (_workspaceApplicationsReceived && _workspaceApplications.Length == 0)
            {
                return Task.FromResult(ValidationResult.Fail(
                    "Only allow Workspace apps requires at least one configured Application Launcher."));
            }

            return Task.FromResult(_workspaceApplicationsReceived
                ? ValidationResult.Success
                : ValidationResult.Warn("The workspace allowlist will be checked before the session starts."));
        }

        return _settings.ValidateAsync();
    }

    public void SetWorkspaceApplications(IEnumerable<string> processNames)
    {
        _workspaceApplications = processNames
            .Select(name => Path.GetFileNameWithoutExtension(name.Trim()))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _workspaceApplicationsReceived = true;
    }

    public Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var processes = _settings.IsAllowList ? _workspaceApplications : _settings.GetProcesses().ToArray();
        if (_settings.IsAllowList && processes.Length == 0)
        {
            throw new InvalidOperationException(
                "Only allow Workspace apps requires at least one configured Application Launcher.");
        }

        _logger.LogInfo("Starting App Blocker in {Mode} mode with {Count} processes.",
            _settings.IsAllowList ? "allowlist" : "blocklist", processes.Length);

        try
        {
            var killed = _settings.IsAllowList ? _blocker.AllowOnly(processes) : _blocker.Block(processes);
            _isBlocking = true;
            _pausedForBreak = false;

            if (killed.Count > 0)
            {
                var appList = string.Join(", ", killed);
                // _notifier.ShowSystemAsync("Focus Mode: Distractions Cleared", $"Closed apps: {appList}");
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
        _isBlocking = false;
        _pausedForBreak = false;
        _lastNotificationTime.Clear();
        return Task.CompletedTask;
    }

    public Task PauseForBreakAsync(CancellationToken cancellationToken)
    {
        _isBlocking = false;
        _pausedForBreak = true;
        _blocker.UnblockAll();
        return Task.CompletedTask;
    }

    public Task ResumeAfterBreakAsync(CancellationToken cancellationToken)
    {
        _pausedForBreak = false;
        if (_settings.IsAllowList)
            _blocker.AllowOnly(_workspaceApplications);
        else
            _blocker.Block(_settings.GetProcesses());
        _isBlocking = true;
        return Task.CompletedTask;
    }

    public Task<bool> IsProtectionHealthyAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_pausedForBreak || _isBlocking && _blocker.IsMonitoring);

    public Task<bool> CanStartCommittedSessionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(!_settings.IsAllowList || _workspaceApplications.Length > 0);

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
            _isBlocking = false;
            _pausedForBreak = false;
        }
        catch
        {
            // Ignore errors during dispose
        }

        GC.SuppressFinalize(this);
    }
}
