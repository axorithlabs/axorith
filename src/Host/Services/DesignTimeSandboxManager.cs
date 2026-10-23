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
    private sealed class Sandbox(Guid moduleId, IModule module, ILifetimeScope? scope)
    {
        public Guid ModuleId { get; } = moduleId;
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
    // shortcut: serialize sandbox lifecycles; use per-instance gates if parallel initialization becomes necessary.
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private bool _disposed;
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
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sandboxes.TryGetValue(instanceId, out var sandbox))
            {
                EnsureCapacity();
                var (module, scope) = _moduleRegistry.CreateInstance(moduleId);
                if (module == null)
                {
                    scope?.Dispose();
                    throw new InvalidOperationException($"Failed to create module {moduleId}");
                }

                sandbox = new Sandbox(moduleId, module, scope);
                _sandboxes[instanceId] = sandbox;
                _logger.LogInformation("Design-time sandbox created for {InstanceId} ({ModuleId})", instanceId, moduleId);
            }

            if (sandbox.ModuleId != moduleId)
                throw new InvalidOperationException($"Sandbox {instanceId} belongs to another module.");

            sandbox.LastAccessUtc = DateTime.UtcNow;
            if (!sandbox.InitDone)
            {
                try
                {
                    using var initCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    initCts.CancelAfter(TimeSpan.FromSeconds(5));
                    await sandbox.Module.InitializeAsync(initCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    DisposeSandboxCore(instanceId);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error initializing sandbox {InstanceId} ({ModuleId})", instanceId, moduleId);
                }
            }

            try
            {
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

                BroadcastAll(instanceId, sandbox);
            }
            catch
            {
                DisposeSandboxCore(instanceId);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public IModule? GetModule(Guid instanceId)
    {
        _lifecycleGate.Wait();
        try
        {
            if (_sandboxes.TryGetValue(instanceId, out var sb))
            {
                sb.LastAccessUtc = DateTime.UtcNow;
                return sb.Module;
            }

            return null;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void ApplySetting(Guid instanceId, string key, string? stringValue)
    {
        _lifecycleGate.Wait();
        try
        {
            if (!_sandboxes.TryGetValue(instanceId, out var sb))
                throw new InvalidOperationException($"Sandbox not found for {instanceId}");

            sb.LastAccessUtc = DateTime.UtcNow;
            var setting = sb.Module.GetSettings().FirstOrDefault(s => s.Key == key);
            if (setting == null)
            {
                _logger.LogWarning("Setting {Key} not found for sandbox {InstanceId}", key, instanceId);
                return;
            }

            setting.SetValueFromString(stringValue);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<bool> TryInvokeActionAsync(Guid instanceId, string actionKey, CancellationToken ct)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_sandboxes.TryGetValue(instanceId, out var sb))
                return false;

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
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void DisposeSandbox(Guid instanceId)
    {
        _lifecycleGate.Wait();
        try
        {
            DisposeSandboxCore(instanceId);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void DisposeSandboxCore(Guid instanceId)
    {
        if (!_sandboxes.TryRemove(instanceId, out var sb))
            return;

        try
        {
            _broadcaster.UnsubscribeModuleInstance(instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error unsubscribing module {InstanceId}", instanceId);
        }

        try
        {
            if (sb.Scope != null) sb.Scope.Dispose();
            else sb.Module.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing sandbox {InstanceId}", instanceId);
        }
        _logger.LogInformation("Design-time sandbox disposed for {InstanceId}", instanceId);
    }

    public void DisposeSandboxesForPreset(IEnumerable<Guid> moduleInstanceIds)
    {
        foreach (var instanceId in moduleInstanceIds)
        {
            DisposeSandbox(instanceId);
        }
    }

    public void ReBroadcast(Guid instanceId)
    {
        _lifecycleGate.Wait();
        try
        {
            if (!_sandboxes.TryGetValue(instanceId, out var sb))
                throw new InvalidOperationException($"Sandbox not found for {instanceId}");

            sb.LastAccessUtc = DateTime.UtcNow;

            BroadcastAll(instanceId, sb);
        }
        finally
        {
            _lifecycleGate.Release();
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

    private void BroadcastAll(Guid instanceId, Sandbox sb)
    {
        foreach (var setting in sb.Module.GetSettings())
        {
            _ = _broadcaster.BroadcastUpdateAsync(instanceId, setting.Key,
                SettingProperty.Value, setting.GetCurrentValueAsObject());
            _ = _broadcaster.BroadcastUpdateAsync(instanceId, setting.Key,
                SettingProperty.Visibility, setting.GetCurrentVisibility());
            _ = _broadcaster.BroadcastUpdateAsync(instanceId, setting.Key,
                SettingProperty.Label, setting.GetCurrentLabel());
            _ = _broadcaster.BroadcastUpdateAsync(instanceId, setting.Key,
                SettingProperty.ReadOnly, setting.GetCurrentReadOnly());

            var choices = setting.GetCurrentChoices();
            if (choices != null)
            {
                _ = _broadcaster.BroadcastUpdateAsync(instanceId, setting.Key,
                    SettingProperty.Choices, choices);
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

    private void EnsureCapacity()
    {
        while (_sandboxes.Count >= _maxSandboxes)
        {
            var oldest = _sandboxes.MinBy(kv => kv.Value.LastAccessUtc);
            DisposeSandboxCore(oldest.Key);
        }
    }

    private void EvictIdle(object? _)
    {
        if (!_lifecycleGate.Wait(0)) return;
        try
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _sandboxes)
            {
                if (now - kv.Value.LastAccessUtc > _idleTtl)
                    DisposeSandboxCore(kv.Key);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Dispose()
    {
        _evictionTimer.Dispose();
        _sessionManager.SessionStarted -= OnSessionStarted;
        _lifecycleGate.Wait();
        try
        {
            _disposed = true;
            foreach (var id in _sandboxes.Keys.ToArray())
                DisposeSandboxCore(id);
        }
        finally
        {
            _lifecycleGate.Release();
        }
        GC.SuppressFinalize(this);
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
