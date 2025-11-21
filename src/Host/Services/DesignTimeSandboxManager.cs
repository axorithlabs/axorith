using System.Collections.Concurrent;
using Autofac;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Streaming;
using Axorith.Sdk;
using Microsoft.Extensions.Options;

namespace Axorith.Host.Services;

public sealed class DesignTimeSandboxManager : IDisposable
{
    private sealed class Sandbox(IModule module, ILifetimeScope? scope)
    {
        public IModule Module { get; } = module;
        public ILifetimeScope? Scope { get; } = scope;
        public DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
        public bool InitDone { get; set; }
        public bool Subscribed { get; set; }
    }

    private readonly IModuleRegistry _moduleRegistry;
    private readonly SettingUpdateBroadcaster _broadcaster;
    private readonly ILogger<DesignTimeSandboxManager> _logger;
    private readonly ISessionManager _sessionManager;

    private readonly ConcurrentDictionary<Guid, Sandbox> _sandboxes = new();
    private readonly Timer _evictionTimer;

    private readonly TimeSpan _idleTtl;
    private readonly int _maxSandboxes;

    public DesignTimeSandboxManager(IModuleRegistry moduleRegistry,
        SettingUpdateBroadcaster broadcaster,
        ILogger<DesignTimeSandboxManager> logger,
        IOptions<Configuration> options,
        ISessionManager sessionManager)
    {
        _moduleRegistry = moduleRegistry;
        _broadcaster = broadcaster;
        _logger = logger;
        _sessionManager = sessionManager;
        var cfg = options.Value.DesignTime;
        _idleTtl = TimeSpan.FromSeconds(Math.Clamp(cfg?.SandboxIdleTtlSeconds ?? 300, 30, 86400));
        _maxSandboxes = Math.Clamp(cfg?.MaxSandboxes ?? 5, 1, 1000);

        _evictionTimer = new Timer(EvictIdle, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _sessionManager.SessionStarted += OnSessionStarted;
    }

    public async Task EnsureAsync(Guid instanceId, Guid moduleId, IReadOnlyDictionary<string, string?> initial,
        CancellationToken ct)
    {
        var sandbox = _sandboxes.GetOrAdd(instanceId, _ =>
        {
            EnsureCapacity();
            var (module, scope) = _moduleRegistry.CreateInstance(moduleId);
            if (module == null)
            {
                throw new InvalidOperationException($"Failed to create module {moduleId}");
            }

            var sb = new Sandbox(module, scope);
            _logger.LogInformation("Design-time sandbox created for {InstanceId} ({ModuleId})", instanceId, moduleId);
            return sb;
        });

        sandbox.LastAccessUtc = DateTime.UtcNow;
        if (!sandbox.InitDone)
        {
            try
            {
                using var initCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                initCts.CancelAfter(TimeSpan.FromSeconds(5));
                await sandbox.Module.InitializeAsync(initCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error initializing sandbox {InstanceId} ({ModuleId})", instanceId, moduleId);
            }
        }

        if (!sandbox.Subscribed)
        {
            SubscribeAll(instanceId, sandbox);
            sandbox.Subscribed = true;
        }

        if (!sandbox.InitDone)
        {
            ApplyAll(sandbox, initial);
            sandbox.InitDone = true;
        }
    }

    public void ApplySetting(Guid instanceId, string key, string? stringValue)
    {
        if (!_sandboxes.TryGetValue(instanceId, out var sb))
        {
            throw new InvalidOperationException($"Sandbox not found for {instanceId}");
        }

        sb.LastAccessUtc = DateTime.UtcNow;
        var setting = sb.Module.GetSettings().FirstOrDefault(s => s.Key == key);
        if (setting == null)
        {
            _logger.LogWarning("Setting {Key} not found for sandbox {InstanceId}", key, instanceId);
            return;
        }

        setting.SetValueFromString(stringValue);
    }

    /// <summary>
    ///     Attempts to invoke an action on an existing design-time sandbox module instance.
    ///     Returns false if no sandbox exists for the given instance ID.
    /// </summary>
    public async Task<bool> TryInvokeActionAsync(Guid instanceId, string actionKey, CancellationToken ct)
    {
        if (!_sandboxes.TryGetValue(instanceId, out var sb))
        {
            return false;
        }

        sb.LastAccessUtc = DateTime.UtcNow;

        var action = sb.Module.GetActions().FirstOrDefault(a => a.Key == actionKey);
        if (action == null)
        {
            _logger.LogWarning("Action {ActionKey} not found for sandbox {InstanceId}", actionKey, instanceId);
            throw new InvalidOperationException($"Action '{actionKey}' not found for design-time sandbox {instanceId}");
        }

        await action.InvokeAsync().ConfigureAwait(false);
        return true;
    }

    public void DisposeSandbox(Guid instanceId)
    {
        if (!_sandboxes.TryRemove(instanceId, out var sb))
        {
            return;
        }

        try
        {
            _broadcaster.UnsubscribeModuleInstance(instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error unsubscribing module {InstanceId}", instanceId);
        }

        sb.Module.Dispose();
        sb.Scope?.Dispose();
        _logger.LogInformation("Design-time sandbox disposed for {InstanceId}", instanceId);
    }

    public void ReBroadcast(Guid instanceId)
    {
        if (!_sandboxes.TryGetValue(instanceId, out var sb))
        {
            throw new InvalidOperationException($"Sandbox not found for {instanceId}");
        }

        sb.LastAccessUtc = DateTime.UtcNow;

        foreach (var setting in sb.Module.GetSettings())
        {
            _ = _broadcaster.BroadcastUpdateAsync(instanceId, setting.Key,
                SettingProperty.Visibility, setting.GetCurrentVisibility());
            _ = _broadcaster.BroadcastUpdateAsync(instanceId, setting.Key,
                SettingProperty.Label, setting.GetCurrentLabel());
            _ = _broadcaster.BroadcastUpdateAsync(instanceId, setting.Key,
                SettingProperty.ReadOnly, setting.GetCurrentReadOnly());
            // Choices may be null/empty; broadcast only if available
            if (setting.Choices != null)
            {
                // We cannot pull current choices synchronously from IObservable, so skip unless module emits.
            }
        }

        foreach (var action in sb.Module.GetActions())
        {
            _ = _broadcaster.BroadcastUpdateAsync(instanceId, action.Key,
                SettingProperty.ActionLabel, action.GetCurrentLabel());
            _ = _broadcaster.BroadcastUpdateAsync(instanceId, action.Key,
                SettingProperty.ActionEnabled, action.GetCurrentEnabled());
        }
    }

    private static void ApplyAll(Sandbox sb, IReadOnlyDictionary<string, string?> initial)
    {
        var dict = sb.Module.GetSettings().ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in initial)
        {
            if (dict.TryGetValue(k, out var s))
            {
                s.SetValueFromString(v);
            }
        }
    }

    private void SubscribeAll(Guid instanceId, Sandbox sb)
    {
        foreach (var s in sb.Module.GetSettings())
        {
            _broadcaster.SubscribeToSetting(instanceId, s);
        }

        foreach (var a in sb.Module.GetActions())
        {
            _broadcaster.SubscribeToAction(instanceId, a);
        }
    }

    private void EnsureCapacity()
    {
        while (_sandboxes.Count >= _maxSandboxes)
        {
            var oldest = _sandboxes.OrderBy(kv => kv.Value.LastAccessUtc).FirstOrDefault();
            if (oldest.Key == Guid.Empty)
            {
                break;
            }

            DisposeSandbox(oldest.Key);
        }
    }

    private void EvictIdle(object? _)
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _sandboxes)
        {
            if (now - kv.Value.LastAccessUtc > _idleTtl)
            {
                DisposeSandbox(kv.Key);
            }
        }
    }

    public void Dispose()
    {
        _evictionTimer.Dispose();

        foreach (var id in _sandboxes.Keys.ToArray())
        {
            DisposeSandbox(id);
        }

        _sandboxes.Clear();
        GC.SuppressFinalize(this);
        _sessionManager.SessionStarted -= OnSessionStarted;
    }

    private void OnSessionStarted(Guid presetId)
    {
        try
        {
            var toDispose = _sandboxes.Keys.ToArray();
            foreach (var id in toDispose)
            {
                try
                {
                    DisposeSandbox(id);
                }
                catch
                {
                    /* best effort */
                }
            }

            _logger.LogInformation("Disposed all design-time sandboxes on session start {PresetId}", presetId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose design-time sandboxes on session start");
        }
    }
}