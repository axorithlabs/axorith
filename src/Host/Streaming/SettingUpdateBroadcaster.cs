using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
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
    private readonly ConcurrentDictionary<Guid, CompositeDisposable> _moduleSubscriptions = new();
    private readonly ConcurrentDictionary<string, string> _lastChoicesFingerprint = new();
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
        if (_moduleSubscriptions.TryRemove(moduleInstanceId, out var disposables))
        {
            disposables.Dispose();
            _logger.LogDebug("Unsubscribed from module instance {InstanceId}", moduleInstanceId);
        }

        // Cleanup choice cache for this module
        var prefix = moduleInstanceId + ":";
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

        _subscribers.AddOrUpdate(key,
            _ => subscriber,
            (_, oldSubscriber) =>
            {
                _logger.LogWarning("Client {Key} already subscribed, replacing stream atomically", key);
                try
                {
                    oldSubscriber.Cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore
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

            UnsubscribeModuleInstance(configuredModule.InstanceId);
            
            var disposables = new CompositeDisposable();
            _moduleSubscriptions[configuredModule.InstanceId] = disposables;

            var settings = moduleInstance.GetSettings();
            foreach (var setting in settings)
            {
                SubscribeToSetting(configuredModule.InstanceId, setting, disposables);
            }

            var actions = moduleInstance.GetActions();
            foreach (var action in actions)
            {
                SubscribeToAction(configuredModule.InstanceId, action, disposables);
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
        _logger.LogDebug("Session stopped: {PresetId}. Cleaning up all runtime subscriptions.", presetId);
        
        foreach (var key in _moduleSubscriptions.Keys.ToArray())
        {
            UnsubscribeModuleInstance(key);
        }
        
        _logger.LogInformation("All setting subscriptions cleared.");
    }

    public Task BroadcastUpdateAsync(Guid moduleInstanceId, string settingKey,
        SettingProperty property, object? value)
    {
        if (_disposed || _subscribers.IsEmpty)
        {
            return Task.CompletedTask;
        }

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
    ///     Subscribes to a module setting's observables.
    ///     Used internally by OnSessionStarted (with disposables list) or externally by DesignTimeSandboxManager (without list, manages own lifecycle).
    /// </summary>
    public void SubscribeToSetting(Guid moduleInstanceId, ISetting setting, CompositeDisposable? disposables = null)
    {
        ArgumentNullException.ThrowIfNull(setting);

        // If no disposables provided (e.g. DesignTime), create/get one for this instance
        if (disposables == null)
        {
            disposables = _moduleSubscriptions.GetOrAdd(moduleInstanceId, _ => new CompositeDisposable());
        }

        setting.ValueAsObject
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(_valueBatchWindowMs))
            .Subscribe(value =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Value, value);
            })
            .DisposeWith(disposables);

        setting.Label
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(label =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Label, label);
            })
            .DisposeWith(disposables);

        setting.IsVisible
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(visible =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Visibility, visible);
            })
            .DisposeWith(disposables);

        setting.IsReadOnly
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(readOnly =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.ReadOnly, readOnly);
            })
            .DisposeWith(disposables);

        if (setting.Choices != null)
        {
            setting.Choices
                .Skip(1)
                .Throttle(TimeSpan.FromMilliseconds(_choicesThrottleMs))
                .Subscribe(choices =>
                {
                    _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Choices, choices);
                })
                .DisposeWith(disposables);
        }
    }

    public void SubscribeToAction(Guid moduleInstanceId, IAction action, CompositeDisposable? disposables = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (disposables == null)
        {
            disposables = _moduleSubscriptions.GetOrAdd(moduleInstanceId, _ => new CompositeDisposable());
        }

        action.Label
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(label =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, action.Key, SettingProperty.ActionLabel, label);
            })
            .DisposeWith(disposables);

        action.IsEnabled
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(enabled =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, action.Key, SettingProperty.ActionEnabled, enabled);
            })
            .DisposeWith(disposables);
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

        foreach (var disposables in _moduleSubscriptions.Values)
        {
            disposables.Dispose();
        }
        _moduleSubscriptions.Clear();

        _subscribers.Clear();

        _logger.LogInformation("SettingUpdateBroadcaster disposed");

        GC.SuppressFinalize(this);
    }
}