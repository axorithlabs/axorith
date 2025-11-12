using System.Collections.Concurrent;
using System.Reactive.Linq;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Mappers;
using Axorith.Sdk.Settings;
using Grpc.Core;

namespace Axorith.Host.Streaming;

/// <summary>
///     Broadcasts reactive setting updates from active module instances to all connected gRPC clients.
///     Subscribes to module setting observables and converts changes to gRPC streams.
/// </summary>
public class SettingUpdateBroadcaster : IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SettingUpdateBroadcaster> _logger;
    private readonly ConcurrentDictionary<string, IServerStreamWriter<SettingUpdate>> _subscribers = new();
    private readonly ConcurrentDictionary<string, IDisposable> _settingSubscriptions = new();
    private readonly ConcurrentDictionary<string, string> _lastChoicesFingerprint = new();
    private bool _disposed;

    public SettingUpdateBroadcaster(ISessionManager sessionManager, ILogger<SettingUpdateBroadcaster> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to session lifecycle to manage setting subscriptions
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
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                try { sub.Dispose(); } catch { /* ignore */ }
                toRemove.Add(key);
            }
        }
        foreach (var k in toRemove) _settingSubscriptions.TryRemove(k, out _);

        // Clear cached choices fingerprints for this module instance
        var choiceKeys = _lastChoicesFingerprint.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var ck in choiceKeys) _lastChoicesFingerprint.TryRemove(ck, out _);
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

        if (!_subscribers.TryAdd(key, stream))
        {
            _logger.LogWarning("Client {Key} already subscribed, replacing stream", key);
            _subscribers[key] = stream;
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client {Key} unsubscribed (cancelled)", key);
        }
        finally
        {
            _subscribers.TryRemove(key, out _);
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

        // Iterate through all configured modules in the active preset
        foreach (var configuredModule in activePreset.Modules)
        {
            // Get the live module instance by InstanceId (not ModuleId!)
            var moduleInstance = _sessionManager.GetActiveModuleInstanceByInstanceId(configuredModule.InstanceId);
            if (moduleInstance == null)
            {
                _logger.LogWarning("Module instance not found for InstanceId: {InstanceId}", configuredModule.InstanceId);
                continue;
            }

            // Subscribe to all settings of this module
            var settings = moduleInstance.GetSettings();
            foreach (var setting in settings)
            {
                SubscribeToSetting(configuredModule.InstanceId, setting);
            }

            _logger.LogDebug("Subscribed to {SettingCount} settings for module {ModuleName} (InstanceId: {InstanceId})",
                settings.Count, configuredModule.CustomName ?? "Unknown", configuredModule.InstanceId);
        }

        _logger.LogInformation("Successfully subscribed to settings for {ModuleCount} modules", activePreset.Modules.Count);
    }

    private void OnSessionStopped(Guid presetId)
    {
        // Dispose all setting subscriptions
        var cleared = _settingSubscriptions.Count;
        foreach (var (_, subscription) in _settingSubscriptions) subscription.Dispose();
        _settingSubscriptions.Clear();
        _lastChoicesFingerprint.Clear();

        _logger.LogDebug("Session stopped: {PresetId}, cleared {Count} setting subscriptions",
            presetId, cleared);
    }

    /// <summary>
    ///     Manually broadcasts a setting update (used when setting updated via gRPC).
    /// </summary>
    public async Task BroadcastUpdateAsync(Guid moduleInstanceId, string settingKey,
        SettingProperty property, object? value)
    {
        if (_disposed || _subscribers.IsEmpty)
            return;

        // Choices caching: skip duplicate broadcasts with same fingerprint
        if (property == SettingProperty.Choices && value is IReadOnlyList<KeyValuePair<string, string>> choicesList)
        {
            var fingerprint = string.Join("\n", choicesList.Select(kv => kv.Key + "\u0001" + kv.Value));
            var cacheKey = $"{moduleInstanceId}:{settingKey}:choices";
            if (_lastChoicesFingerprint.TryGetValue(cacheKey, out var prev) && prev == fingerprint)
                return;
            _lastChoicesFingerprint[cacheKey] = fingerprint;
        }

        var update = SettingMapper.CreateUpdate(moduleInstanceId, settingKey, property, value);

        _logger.LogDebug("Broadcast setting update: {ModuleId}.{Key}.{Property} = {Value}",
            moduleInstanceId, settingKey, property, value);

        var tasks = new List<Task>();

        foreach (var (subscriberKey, stream) in _subscribers)
        {
            // Key may be 'subscriberId' or 'subscriberId:moduleInstanceId'
            string? filter = null;
            var sepIndex = subscriberKey.IndexOf(':');
            if (sepIndex > 0 && sepIndex < subscriberKey.Length - 1)
                filter = subscriberKey[(sepIndex + 1)..];

            if (filter != null && !string.Equals(filter, moduleInstanceId.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await stream.WriteAsync(update).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to send setting update to subscriber {SubscriberId}, removing",
                        subscriberKey);
                    _subscribers.TryRemove(subscriberKey, out _);
                }
            }));
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Individual exceptions already logged
        }
    }

    /// <summary>
    ///     Subscribes to a module setting's observables and broadcasts changes.
    ///     Called internally when session starts and modules are initialized.
    /// </summary>
    public void SubscribeToSetting(Guid moduleInstanceId, ISetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var keyPrefix = $"{moduleInstanceId}:{setting.Key}";

        // Subscribe to Value changes
        var valueSub = setting.ValueAsObject
            .Skip(1) // Skip initial value
            .Subscribe(value =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Value, value);
            });

        _settingSubscriptions[$"{keyPrefix}:value"] = valueSub;

        // Subscribe to Label changes
        var labelSub = setting.Label
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(label =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Label, label);
            });

        _settingSubscriptions[$"{keyPrefix}:label"] = labelSub;

        // Subscribe to Visibility changes
        var visSub = setting.IsVisible
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(visible =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Visibility, visible);
            });

        _settingSubscriptions[$"{keyPrefix}:visibility"] = visSub;

        // Subscribe to ReadOnly changes
        var readOnlySub = setting.IsReadOnly
            .Skip(1)
            .DistinctUntilChanged()
            .Subscribe(readOnly =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.ReadOnly, readOnly);
            });

        _settingSubscriptions[$"{keyPrefix}:readonly"] = readOnlySub;

        // Subscribe to Choices changes (if applicable)
        if (setting.Choices != null)
        {
            var choicesSub = setting.Choices
                .Skip(1)
                .Throttle(TimeSpan.FromMilliseconds(200))
                .Subscribe(choices =>
                {
                    _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Choices, choices);
                });

            _settingSubscriptions[$"{keyPrefix}:choices"] = choicesSub;
        }

        _logger.LogDebug("Subscribed to setting updates: {ModuleId}.{Key}", moduleInstanceId, setting.Key);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _sessionManager.SessionStarted -= OnSessionStarted;
        _sessionManager.SessionStopped -= OnSessionStopped;

        foreach (var subscription in _settingSubscriptions.Values) subscription.Dispose();
        _settingSubscriptions.Clear();

        _subscribers.Clear();

        _logger.LogInformation("SettingUpdateBroadcaster disposed");

        GC.SuppressFinalize(this);
    }
}