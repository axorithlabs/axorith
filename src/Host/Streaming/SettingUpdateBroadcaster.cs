// ===== FILE: src\Host\Streaming\SettingUpdateBroadcaster.cs =====
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Threading.Channels;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Axorith.Host.Streaming;

/// <summary>
///     Broadcasts reactive setting updates from active module instances to all connected gRPC clients.
///     Subscribes to module setting observables and converts changes to gRPC streams.
/// </summary>
public class SettingUpdateBroadcaster : IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SettingUpdateBroadcaster> _logger;

    private sealed class Subscriber
    {
        public required IServerStreamWriter<SettingUpdate> Stream { get; init; }
        public required Channel<SettingUpdate> Queue { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required Task Loop { get; init; }
    }

    private readonly ConcurrentDictionary<string, Subscriber> _subscribers = new();
    private readonly ConcurrentDictionary<string, IDisposable> _settingSubscriptions = new();
    private readonly ConcurrentDictionary<string, string> _lastChoicesFingerprint = new();
    private readonly ConcurrentDictionary<Guid, byte> _runtimeInstanceIds = new();
    private bool _disposed;

    private readonly int _choicesThrottleMs;
    private readonly int _valueBatchWindowMs;

    public SettingUpdateBroadcaster(ISessionManager sessionManager, ILogger<SettingUpdateBroadcaster> logger,
        IOptions<Configuration>? options)
    {
        _sessionManager = sessionManager;
        _logger = logger;
        var streaming = options?.Value.Streaming;
        _choicesThrottleMs = Math.Clamp(streaming?.ChoicesThrottleMs ?? 200, 0, 10_000);
        _valueBatchWindowMs = Math.Clamp(streaming?.ValueBatchWindowMs ?? 16, 0, 1000);

        _sessionManager.SessionStarted += OnSessionStarted;
        _sessionManager.SessionStopped += OnSessionStopped;

        _logger.LogInformation("SettingUpdateBroadcaster initialized");
    }

    /// <summary>
    ///     Unsubscribes and removes all tracked subscriptions for a specific module instance.
    /// </summary>
    public void UnsubscribeModuleInstance(Guid moduleInstanceId)
    {
        var prefix = moduleInstanceId.ToString();
        var toRemove = new List<string>();
        foreach (var (key, sub) in _settingSubscriptions)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sub.Dispose();

            toRemove.Add(key);
        }

        foreach (var k in toRemove)
        {
            _settingSubscriptions.TryRemove(k, out _);
        }

        var choiceKeys = _lastChoicesFingerprint.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var ck in choiceKeys)
        {
            _lastChoicesFingerprint.TryRemove(ck, out _);
        }
    }

    /// <summary>
    ///     Subscribes a gRPC client to setting updates.
    ///     Optionally filtered by module instance ID.
    /// </summary>
    public async Task SubscribeAsync(string subscriberId, string? moduleInstanceId,
        IServerStreamWriter<SettingUpdate> stream, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        ArgumentNullException.ThrowIfNull(stream);

        _logger.LogInformation("Client {SubscriberId} subscribed to setting updates (filter: {Filter})",
            subscriberId, moduleInstanceId ?? "all");

        var key = string.IsNullOrWhiteSpace(moduleInstanceId) ? subscriberId : $"{subscriberId}:{moduleInstanceId}";

        var channel = Channel.CreateBounded<SettingUpdate>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loopTask = Task.Run(async () =>
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
                while (channel.Reader.TryRead(out var update))
                    try
                    {
                        await stream.WriteAsync(update, linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send setting update to subscriber {SubscriberId}, removing",
                            key);
                        await linkedCts.CancelAsync().ConfigureAwait(false);
                        return;
                    }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, linkedCts.Token);

        var subscriber = new Subscriber
        {
            Stream = stream,
            Queue = channel,
            Cts = linkedCts,
            Loop = loopTask
        };

        // FIX AXOR-39: Use atomic AddOrUpdate instead of TryAdd/TryRemove dance.
        _subscribers.AddOrUpdate(key, 
            // Factory for adding new key
            _ => subscriber, 
            // Factory for updating existing key
            (_, oldSubscriber) => 
            {
                _logger.LogWarning("Client {Key} already subscribed, replacing stream atomically", key);
                
                // Signal the old subscriber to stop. 
                // Cancel() is thread-safe. We don't need to await it here; 
                // the background loopTask will catch the cancellation and exit gracefully.
                try 
                {
                    oldSubscriber.Cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if already disposed
                }

                return subscriber;
            });

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client {Key} unsubscribed (cancelled)", key);
        }
        finally
        {
            // Only remove if it's still OUR subscriber (handle race where it was replaced again)
            if (_subscribers.TryRemove(new KeyValuePair<string, Subscriber>(key, subscriber)))
            {
                await subscriber.Cts.CancelAsync().ConfigureAwait(false);
                subscriber.Cts.Dispose();
            }
        }
    }

    private void OnSessionStarted(Guid presetId)
    {
        _logger.LogDebug("Session started: {PresetId}, subscribing to all module settings", presetId);

        var activePreset = _sessionManager.ActiveSession;
        if (activePreset == null)
        {
            _logger.LogWarning("Session started event fired but ActiveSession is null");
            return;
        }

        foreach (var configuredModule in activePreset.Modules)
        {
            var moduleInstance = _sessionManager.GetActiveModuleInstanceByInstanceId(configuredModule.InstanceId);
            if (moduleInstance == null)
            {
                _logger.LogWarning("Module instance not found for InstanceId: {InstanceId}",
                    configuredModule.InstanceId);
                continue;
            }

            _runtimeInstanceIds[configuredModule.InstanceId] = 1;

            var settings = moduleInstance.GetSettings();
            foreach (var setting in settings)
            {
                SubscribeToSetting(configuredModule.InstanceId, setting);
            }

            var actions = moduleInstance.GetActions();
            foreach (var action in actions)
            {
                SubscribeToAction(configuredModule.InstanceId, action);
            }

            _logger.LogDebug(
                "Subscribed to {SettingCount} settings and {ActionCount} actions for module {ModuleName} (InstanceId: {InstanceId})",
                settings.Count, actions.Count, configuredModule.CustomName ?? "Unknown", configuredModule.InstanceId);
        }

        _logger.LogInformation("Successfully subscribed to settings for {ModuleCount} modules",
            activePreset.Modules.Count);
    }

    private void OnSessionStopped(Guid presetId)
    {
        var removedSubs = 0;
        var removedChoices = 0;

        foreach (var instanceId in _runtimeInstanceIds.Keys.ToArray())
        {
            var prefix = instanceId + ":";
            foreach (var kv in _settingSubscriptions.ToArray())
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kv.Value.Dispose();
                if (_settingSubscriptions.TryRemove(kv.Key, out _))
                {
                    removedSubs++;
                }
            }

            removedChoices += _lastChoicesFingerprint.Keys.ToArray()
                .Where(ck => ck.StartsWith(instanceId + ":", StringComparison.OrdinalIgnoreCase))
                .Count(ck => _lastChoicesFingerprint.TryRemove(ck, out _));

            _runtimeInstanceIds.TryRemove(instanceId, out _);
        }

        _logger.LogDebug(
            "Session stopped: {PresetId}, removed {Subs} runtime subscriptions and {Choices} choice caches",
            presetId, removedSubs, removedChoices);
    }

    /// <summary>
    ///     Manually broadcasts a setting update (used when setting updated via gRPC).
    /// </summary>
    public Task BroadcastUpdateAsync(Guid moduleInstanceId, string settingKey,
        SettingProperty property, object? value)
    {
        if (_disposed || _subscribers.IsEmpty)
        {
            return Task.CompletedTask;
        }

        // Choices caching: skip duplicate broadcasts with same fingerprint
        if (property == SettingProperty.Choices && value is IReadOnlyList<KeyValuePair<string, string>> choicesList)
        {
            var fingerprint = string.Join("\n", choicesList.Select(kv => kv.Key + "\u0001" + kv.Value));
            var cacheKey = $"{moduleInstanceId}:{settingKey}:choices";
            if (_lastChoicesFingerprint.TryGetValue(cacheKey, out var prev) && prev == fingerprint)
            {
                return Task.CompletedTask;
            }

            _lastChoicesFingerprint[cacheKey] = fingerprint;
        }

        var update = SettingMapper.CreateUpdate(moduleInstanceId, settingKey, property, value);

        _logger.LogDebug("Broadcast setting update: {ModuleId}.{Key}.{Property} = {Value}",
            moduleInstanceId, settingKey, property, value);

        foreach (var (subscriberKey, sub) in _subscribers)
        {
            string? filter = null;
            var sepIndex = subscriberKey.IndexOf(':');
            if (sepIndex > 0 && sepIndex < subscriberKey.Length - 1)
            {
                filter = subscriberKey[(sepIndex + 1)..];
            }

            if (filter != null &&
                !string.Equals(filter, moduleInstanceId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!sub.Queue.Writer.TryWrite(update))
            {
                _logger.LogDebug("Subscriber queue full for {SubscriberId}, dropping oldest", subscriberKey);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Subscribes to a module setting's observables and broadcasts changes.
    ///     Called internally when session starts and modules are initialized.
    /// </summary>
    public void SubscribeToSetting(Guid moduleInstanceId, ISetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var keyPrefix = $"{moduleInstanceId}:{setting.Key}";

        var valueSub = setting.ValueAsObject
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(_valueBatchWindowMs))
            .Subscribe(value =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Value, value);
            });
        ReplaceSubscription($"{keyPrefix}:value", valueSub);

        var labelSub = setting.Label
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(label =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Label, label);
            });
        ReplaceSubscription($"{keyPrefix}:label", labelSub);

        var visSub = setting.IsVisible
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(visible =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Visibility, visible);
            });
        ReplaceSubscription($"{keyPrefix}:visibility", visSub);

        var readOnlySub = setting.IsReadOnly
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(readOnly =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.ReadOnly, readOnly);
            });
        ReplaceSubscription($"{keyPrefix}:readonly", readOnlySub);

        if (setting.Choices != null)
        {
            var choicesSub = setting.Choices
                .Skip(1)
                .Throttle(TimeSpan.FromMilliseconds(_choicesThrottleMs))
                .Subscribe(choices =>
                {
                    _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Choices, choices);
                });
            ReplaceSubscription($"{keyPrefix}:choices", choicesSub);
        }

        _logger.LogDebug("Subscribed to setting updates: {ModuleId}.{Key}", moduleInstanceId, setting.Key);
    }

    /// <summary>
    ///     Subscribes to a module action's observables and broadcasts changes.
    /// </summary>
    public void SubscribeToAction(Guid moduleInstanceId, IAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var keyPrefix = $"{moduleInstanceId}:{action.Key}";

        var labelSub = action.Label
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(label =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, action.Key, SettingProperty.ActionLabel, label);
            });
        ReplaceSubscription($"{keyPrefix}:action_label", labelSub);

        var enabledSub = action.IsEnabled
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(enabled =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, action.Key, SettingProperty.ActionEnabled, enabled);
            });
        ReplaceSubscription($"{keyPrefix}:action_enabled", enabledSub);

        _logger.LogDebug("Subscribed to action updates: {ModuleId}.{Key}", moduleInstanceId, action.Key);
    }

    private void ReplaceSubscription(string key, IDisposable sub)
    {
        if (_settingSubscriptions.TryRemove(key, out var old))
        {
            old.Dispose();
        }

        _settingSubscriptions[key] = sub;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _sessionManager.SessionStarted -= OnSessionStarted;
        _sessionManager.SessionStopped -= OnSessionStopped;

        foreach (var subscription in _settingSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _settingSubscriptions.Clear();

        _subscribers.Clear();

        _logger.LogInformation("SettingUpdateBroadcaster disposed");

        GC.SuppressFinalize(this);
    }
}