using System.Diagnostics;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axorith.Shared.Utils;
using PostHog;
using PostHog.Api;

namespace Axorith.Telemetry;

public interface ITelemetryService : IAsyncDisposable
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
    void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null);
    void TrackError(Exception exception, string subsystem, string operation, string severity,
        bool handled, bool fatal, IReadOnlyDictionary<string, object?>? properties = null);
    Task FlushAsync(CancellationToken ct = default);
}

public sealed partial class TelemetryService : ITelemetryService
{
    public const string HttpClientName = "PostHog";
    private static readonly TimeSpan ErrorRepeatInterval = TimeSpan.FromMinutes(1);
    private static readonly HashSet<string> ProductAssemblies = [
        "Axorith.Client", "Axorith.Host", "Axorith.Core", "Axorith.Shared.Platform", "Axorith.Sdk", "Axorith.Contracts"];

    private sealed class ErrorAggregate(Dictionary<string, object?> properties)
    {
        public Dictionary<string, object?> Properties { get; } = properties;
        public DateTimeOffset LastEmittedAt { get; set; } = DateTimeOffset.UtcNow;
        public long PendingCount { get; set; }
        public bool PendingFatal { get; set; }
    }

    private readonly PostHogClient? _client;
    private readonly IReadOnlyDictionary<string, object?> _baseProperties;
    private readonly object _errorLock = new();
    private readonly object _flushLock = new();
    private readonly object _consentLock = new();
    private readonly Dictionary<string, ErrorAggregate> _errors = new(StringComparer.Ordinal);
    private Task? _flushTask;
    private readonly string _distinctId = string.Empty;
    private string? _application;
    private string? _appVersion;
    private string? _osVersion;
    private int _enabled;
    private int _identified;
    private int _preferenceGeneration;
    private int _acceptingEvents = 1;
    private volatile bool _disposed;

    public bool IsEnabled => _client is not null && Volatile.Read(ref _enabled) != 0 && !_disposed;

    public TelemetryService(TelemetrySettings settings, IHttpClientFactory? httpClientFactory = null)
    {
        var resolved = (settings ?? throw new ArgumentNullException(nameof(settings))).WithEnvironmentOverrides();
        _baseProperties = new Dictionary<string, object?>();
        if (!resolved.IsConfigured)
        {
            return;
        }

        _distinctId = string.IsNullOrWhiteSpace(resolved.DistinctId)
            ? DeviceIdProvider.GetDeviceId()
            : resolved.DistinctId;

        var assembly = typeof(TelemetryService).Assembly;
        var version = string.IsNullOrWhiteSpace(resolved.AppVersion)
            ? assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
              ?? assembly.GetName().Version?.ToString() ?? "unknown"
            : resolved.AppVersion;
        var osVersion = string.IsNullOrWhiteSpace(resolved.OsVersion)
            ? Environment.OSVersion.VersionString
            : resolved.OsVersion;
        _baseProperties = BuildBaseProperties(resolved, version, osVersion);
        _application = resolved.ApplicationName;
        _appVersion = version;
        _osVersion = osVersion;

        var flushAt = Math.Max(1, resolved.BatchSize);
        var options = new PostHogOptions
        {
            ProjectToken = resolved.PostHogApiKey,
            HostUrl = new Uri(resolved.PostHogHost),
            FlushAt = flushAt,
            MaxBatchSize = flushAt,
            MaxQueueSize = Math.Max(flushAt, resolved.QueueLimit),
            FlushInterval = resolved.FlushInterval,
            MaxRetries = Math.Max(0, resolved.MaxRetryAttempts),
            InitialRetryDelay = resolved.InitialRetryDelay,
            EnableCompression = false,
            IsServer = !string.Equals(resolved.ApplicationName, "Axorith.Client", StringComparison.OrdinalIgnoreCase),
            BeforeSend = FilterCapturedEvent
        };

        _client = new PostHogClient(options, httpClientFactory: new NamedPostHogHttpClientFactory(httpClientFactory));

        SetEnabled(resolved.Enabled);
    }

    public void SetEnabled(bool enabled)
    {
        if (_client is null)
        {
            return;
        }

        bool wasEnabled;
        bool identify;
        lock (_consentLock)
        {
            if (_disposed || Volatile.Read(ref _acceptingEvents) == 0)
            {
                return;
            }

            wasEnabled = Volatile.Read(ref _enabled) != 0;
            if (wasEnabled != enabled)
            {
                if (enabled)
                {
                    Interlocked.Increment(ref _preferenceGeneration);
                    Volatile.Write(ref _enabled, 1);
                }
                else
                {
                    Volatile.Write(ref _enabled, 0);
                    Interlocked.Increment(ref _preferenceGeneration);
                    lock (_errorLock)
                    {
                        foreach (var error in _errors.Values)
                        {
                            error.PendingCount = 0;
                            error.PendingFatal = false;
                        }
                    }
                }
            }

            identify = enabled && (!wasEnabled || Interlocked.Exchange(ref _identified, 1) == 0);
        }

        if (!enabled)
        {
            return;
        }

        if (identify)
        {
            TrackEvent(TelemetryConstants.IdentifyEvent, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.Set] = new Dictionary<string, object?>
                {
                    [TelemetryConstants.Properties.Application] = _application,
                    [TelemetryConstants.Properties.AxorithVersion] = _appVersion,
                    [TelemetryConstants.Properties.OsVersion] = _osVersion
                }
            });
        }
    }

    public void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        try
        {
            lock (_flushLock)
            {
                if (!IsEnabled || Volatile.Read(ref _acceptingEvents) == 0)
                {
                    return;
                }

                var name = string.IsNullOrWhiteSpace(eventName) ? TelemetryConstants.DefaultEvent : eventName;
                if (!EventNameRegex().IsMatch(name))
                {
                    return;
                }

                var safeProperties = properties is null
                    ? new Dictionary<string, object?>()
                    : TelemetryEventSanitizer.SanitizeProperties(properties);
                var eventProperties = new Dictionary<string, object>(StringComparer.Ordinal);
                if (!string.Equals(name, TelemetryConstants.IdentifyEvent, StringComparison.OrdinalIgnoreCase))
                {
                    AddProperties(eventProperties, _baseProperties);
                }

                AddProperties(eventProperties, safeProperties);
                eventProperties[TelemetryConstants.Properties.PreferenceGeneration] =
                    Volatile.Read(ref _preferenceGeneration);
                _client!.Capture(_distinctId, name, eventProperties, groups: null, flags: null,
                    timestamp: DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Telemetry event '{eventName}' was dropped: {ex.GetType().Name}");
        }
    }

    public void TrackError(Exception exception, string subsystem, string operation, string severity,
        bool handled, bool fatal, IReadOnlyDictionary<string, object?>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!IsEnabled)
        {
            return;
        }

        var (fingerprint, diagnostic) = Describe(exception);
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["exceptionType"] = exception.GetType().Name,
            ["fingerprint"] = fingerprint,
            ["diagnostic"] = diagnostic,
            ["subsystem"] = subsystem,
            ["operation"] = operation,
            ["severity"] = severity,
            ["handled"] = handled,
            ["fatal"] = fatal
        };
        if (properties is not null)
        {
            foreach (var (key, value) in properties) details[key] = value;
        }

        var safeDetails = TelemetryEventSanitizer.SanitizeProperties(details)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        safeDetails["occurrenceCount"] = 1;
        var now = DateTimeOffset.UtcNow;
        lock (_errorLock)
        {
            if (!_errors.TryGetValue(fingerprint, out var aggregate))
            {
                _errors[fingerprint] = new ErrorAggregate(safeDetails);
                TrackEvent("ErrorOccurred", safeDetails);
                return;
            }

            aggregate.PendingCount++;
            aggregate.PendingFatal |= fatal;
            if (!fatal && now - aggregate.LastEmittedAt < ErrorRepeatInterval)
            {
                return;
            }

            var repeatProperties = new Dictionary<string, object?>(aggregate.Properties, StringComparer.Ordinal)
            {
                ["occurrenceCount"] = aggregate.PendingCount,
                ["fatal"] = aggregate.PendingFatal
            };
            aggregate.PendingCount = 0;
            aggregate.PendingFatal = false;
            aggregate.LastEmittedAt = now;
            TrackEvent("ErrorOccurred", repeatProperties);
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_client is null || _disposed)
        {
            return;
        }

        FlushPendingErrors();
        Task flushTask;
        lock (_flushLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_flushTask is null)
            {
                Interlocked.Exchange(ref _acceptingEvents, 0);
                _flushTask = DrainClientAsync();
            }

            flushTask = _flushTask;
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await flushTask.WaitAsync(waitCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (waitCts.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync().ConfigureAwait(false);
    }

    private async Task DrainClientAsync()
    {
        try
        {
            await _client!.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Telemetry is best-effort during shutdown.
        }
        finally
        {
            _disposed = true;
        }
    }

    private void FlushPendingErrors()
    {
        if (!IsEnabled) return;
        lock (_errorLock)
        {
            foreach (var error in _errors.Values.Where(item => item.PendingCount > 0))
            {
                var properties = new Dictionary<string, object?>(error.Properties, StringComparer.Ordinal)
                {
                    ["occurrenceCount"] = error.PendingCount,
                    ["fatal"] = error.PendingFatal
                };
                error.PendingCount = 0;
                error.PendingFatal = false;
                error.LastEmittedAt = DateTimeOffset.UtcNow;
                TrackEvent("ErrorOccurred", properties);
            }
        }
    }

    private static (string Fingerprint, string Diagnostic) Describe(Exception exception)
    {
        var frames = new StackTrace(exception, false).GetFrames() ?? [];
        var productFrames = frames
            .Select(frame => frame.GetMethod())
            .Where(method => ProductAssemblies.Contains(method?.DeclaringType?.Assembly.GetName().Name ?? string.Empty))
            .Select(method => $"{method!.DeclaringType!.Name}.{method.Name}")
            .Take(8)
            .ToArray();
        var material = exception.GetType().FullName + "|" + string.Join("|", productFrames);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return (fingerprint, productFrames.FirstOrDefault() ?? exception.GetType().Name);
    }

    private CapturedEvent? FilterCapturedEvent(CapturedEvent capturedEvent)
    {
        var properties = capturedEvent.Properties;
        if (!IsEnabled ||
            !properties.TryGetValue(TelemetryConstants.Properties.PreferenceGeneration, out var generationValue) ||
            generationValue is not int generation || generation != Volatile.Read(ref _preferenceGeneration))
        {
            return null;
        }

        foreach (var key in properties.Keys.ToArray())
        {
            if (key == TelemetryConstants.Properties.PreferenceGeneration ||
                SensitiveDataMasker.IsSensitiveKey(key) || SensitiveDataMasker.IsGeoKey(key))
            {
                properties.Remove(key);
            }
        }

        return capturedEvent;
    }

    private static void AddProperties(Dictionary<string, object> destination,
        IEnumerable<KeyValuePair<string, object?>> properties)
    {
        foreach (var (key, value) in properties)
        {
            if (value is not null)
            {
                destination[key] = ToPostHogValue(value);
            }
        }
    }

    private static object ToPostHogValue(object value) => value switch
    {
        IReadOnlyDictionary<string, object?> dictionary => JsonSerializer.SerializeToElement(
            dictionary.ToDictionary(pair => pair.Key, pair => pair.Value is null ? null : ToPostHogValue(pair.Value))),
        IDictionary<string, object?> dictionary => JsonSerializer.SerializeToElement(
            dictionary.ToDictionary(pair => pair.Key, pair => pair.Value is null ? null : ToPostHogValue(pair.Value))),
        IEnumerable sequence when value is not string => sequence.Cast<object?>()
            .Select(item => item is null ? null : ToPostHogValue(item)).ToArray(),
        _ => value
    };

    private sealed class NamedPostHogHttpClientFactory(IHttpClientFactory? inner) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => inner?.CreateClient(HttpClientName) ??
            new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    private static IReadOnlyDictionary<string, object?> BuildBaseProperties(TelemetrySettings settings,
        string appVersion, string osVersion)
    {
        var baseValues = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TelemetryConstants.Properties.OsVersion] = osVersion,
            [TelemetryConstants.Properties.AxorithVersion] = appVersion,
            [TelemetryConstants.Properties.Application] = settings.ApplicationName,
            [TelemetryConstants.Properties.BuildChannel] = settings.BuildChannel,
            [TelemetryConstants.Properties.Environment] = settings.EnvironmentOverride
        };
        return TelemetryEventSanitizer.SanitizeProperties(baseValues);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^\\$?[A-Za-z][A-Za-z0-9]{0,79}$")]
    private static partial System.Text.RegularExpressions.Regex EventNameRegex();
}

public sealed class NoopTelemetryService : ITelemetryService
{
    public static NoopTelemetryService Instance { get; } = new();

    public bool IsEnabled => false;
    public void SetEnabled(bool enabled) { }
    public void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null) { }
    public void TrackError(Exception exception, string subsystem, string operation, string severity,
        bool handled, bool fatal, IReadOnlyDictionary<string, object?>? properties = null) { }
    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
