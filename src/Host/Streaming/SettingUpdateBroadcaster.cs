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
        // NOTE: In current architecture, we don't have direct access to module instances from SessionManager
        // This would require refactoring SessionManager to expose ActiveModules
        // For MVP, setting updates will be sent via explicit UpdateSetting gRPC calls
        // Future enhancement: Subscribe to all module setting observables here

        _logger.LogDebug("Session started: {PresetId}, setting subscriptions not yet implemented", presetId);
    }

    private void OnSessionStopped(Guid presetId)
    {
        // Dispose all setting subscriptions
        foreach (var (_, subscription) in _settingSubscriptions) subscription.Dispose();
        _settingSubscriptions.Clear();

        _logger.LogDebug("Session stopped: {PresetId}, cleared {Count} setting subscriptions",
            presetId, _settingSubscriptions.Count);
    }

    /// <summary>
    ///     Manually broadcasts a setting update (used when setting updated via gRPC).
    /// </summary>
    public async Task BroadcastUpdateAsync(Guid moduleInstanceId, string settingKey,
        SettingProperty property, object? value)
    {
        if (_disposed || _subscribers.IsEmpty)
            return;

        var update = SettingMapper.CreateUpdate(moduleInstanceId, settingKey, property, value);

        _logger.LogDebug("Broadcasting setting update: {ModuleId}.{Key}.{Property}",
            moduleInstanceId, settingKey, property);

        var tasks = new List<Task>();

        foreach (var (subscriberId, stream) in _subscribers)
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
                        subscriberId);
                    _subscribers.TryRemove(subscriberId, out _);
                }
            }));

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
            .Subscribe(label =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Label, label);
            });

        _settingSubscriptions[$"{keyPrefix}:label"] = labelSub;

        // Subscribe to Visibility changes
        var visSub = setting.IsVisible
            .Skip(1)
            .Subscribe(visible =>
            {
                _ = BroadcastUpdateAsync(moduleInstanceId, setting.Key, SettingProperty.Visibility, visible);
            });

        _settingSubscriptions[$"{keyPrefix}:visibility"] = visSub;

        // Subscribe to ReadOnly changes
        var readOnlySub = setting.IsReadOnly
            .Skip(1)
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