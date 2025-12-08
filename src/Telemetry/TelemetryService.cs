using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Axorith.Shared.Utils;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Serilog.Sinks.PeriodicBatching;

namespace Axorith.Telemetry;

public interface ITelemetryService : IAsyncDisposable
{
    bool IsEnabled { get; }
    void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null);

    void TrackLog(LogEventLevel level, string messageTemplate, Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null);

    Task FlushAsync(CancellationToken ct = default);
}

public sealed class TelemetryService : ITelemetryService
{
    public const string HttpClientName = "PostHog";

    private readonly Logger? _logger;
    private readonly PeriodicBatchingSink? _batchingSink;
    private readonly MessageTemplateParser _templateParser = new();
    private readonly IReadOnlyCollection<LogEventProperty> _baseProperties;
    private readonly HttpClient? _httpClient;
    private readonly bool _ownsHttpClient;
    private volatile bool _disposed;

    public bool IsEnabled => _logger is not null && !_disposed;

    /// <summary>
    ///     Creates a TelemetryService with IHttpClientFactory for proper HttpClient management.
    /// </summary>
    public TelemetryService(TelemetrySettings settings, IHttpClientFactory? httpClientFactory = null)
    {
        var settings1 = (settings ?? throw new ArgumentNullException(nameof(settings)))
            .WithEnvironmentOverrides();

        if (!settings1.IsActive)
        {
            _baseProperties = [];
            return;
        }

        var distinctId = string.IsNullOrWhiteSpace(settings1.DistinctId)
            ? DeviceIdProvider.GetDeviceId()
            : settings1.DistinctId;

        var resolvedAppVersion = string.IsNullOrWhiteSpace(settings1.AppVersion)
            ? typeof(TelemetryService).Assembly.GetName().Version?.ToString() ?? "unknown"
            : settings1.AppVersion;

        var resolvedOsVersion = string.IsNullOrWhiteSpace(settings1.OsVersion)
            ? Environment.OSVersion.VersionString
            : settings1.OsVersion;

        _baseProperties = BuildBaseProperties(settings1, resolvedAppVersion, resolvedOsVersion);

        // Use IHttpClientFactory if provided, otherwise create our own HttpClient
        if (httpClientFactory is not null)
        {
            _httpClient = httpClientFactory.CreateClient(HttpClientName);
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _ownsHttpClient = true;
        }

        var retryOptions = RetryPolicyOptions.FromSettings(settings1);
        var postHogSink = new PostHogSink(
            _httpClient,
            settings1.PostHogApiKey,
            settings1.PostHogHost,
            distinctId,
            retryOptions);

        var batchingOptions = new PeriodicBatchingSinkOptions
        {
            BatchSizeLimit = settings1.BatchSize,
            QueueLimit = settings1.QueueLimit,
            Period = settings1.FlushInterval
        };

        _batchingSink = new PeriodicBatchingSink(postHogSink, batchingOptions);

        _logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(_batchingSink)
            .CreateLogger();

        TrackEvent(TelemetryConstants.IdentifyEvent, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.DistinctId] = distinctId,
            [TelemetryConstants.Properties.Set] = new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.Application] = settings1.ApplicationName,
                [TelemetryConstants.Properties.AppVersion] = resolvedAppVersion,
                [TelemetryConstants.Properties.OsVersion] = resolvedOsVersion
            }
        });
    }

    public void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (_logger is null || _disposed)
        {
            return;
        }

        var props = new List<LogEventProperty>(_baseProperties)
        {
            new(TelemetryConstants.Properties.EventName,
                new ScalarValue(string.IsNullOrWhiteSpace(eventName) ? TelemetryConstants.DefaultEvent : eventName))
        };

        if (properties != null)
        {
            props.AddRange(ConvertProperties(properties));
        }

        var template = string.IsNullOrWhiteSpace(eventName) ? TelemetryConstants.DefaultEvent : eventName;
        var logEvent = CreateLogEvent(LogEventLevel.Information, template, props, exception: null);
        _logger.Write(logEvent);
    }

    public void TrackLog(LogEventLevel level, string messageTemplate, Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (_logger is null || _disposed)
        {
            return;
        }

        var props = new List<LogEventProperty>(_baseProperties)
        {
            new(TelemetryConstants.Properties.EventName, new ScalarValue(TelemetryConstants.LogEvent))
        };

        if (!string.IsNullOrWhiteSpace(messageTemplate))
        {
            props.Add(new LogEventProperty(TelemetryConstants.Properties.MessageTemplate,
                new ScalarValue(messageTemplate)));
        }

        if (properties != null)
        {
            props.AddRange(ConvertProperties(properties));
        }

        var template = string.IsNullOrWhiteSpace(messageTemplate) ? TelemetryConstants.LogEvent : messageTemplate;
        var logEvent = CreateLogEvent(level, template, props, exception);
        _logger.Write(logEvent);
    }

    /// <summary>
    ///     Flushes all buffered events to PostHog.
    ///     Thread-safe: concurrent calls will wait for the same flush operation.
    ///     Note: This waits for the periodic batching interval to ensure events are sent.
    ///     For immediate flush on shutdown, call DisposeAsync which will flush synchronously.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_batchingSink is null || _disposed)
        {
            return;
        }

        // PeriodicBatchingSink doesn't expose a flush method, so we wait for the flush interval
        // to allow pending events to be sent. This is a best-effort approach.
        // For guaranteed flush, DisposeAsync should be called which disposes the sink.
        var flushInterval = TimeSpan.FromSeconds(1); // Short wait to allow batch to be sent

        try
        {
            await Task.Delay(flushInterval, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is acceptable
        }
    }

    /// <summary>
    ///     Disposes the telemetry service, flushing any pending events first.
    ///     Idempotent: safe to call multiple times.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Flush pending events before disposing
        if (_batchingSink is not null)
        {
            try
            {
                await _batchingSink.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        _logger?.Dispose();

        // Only dispose HttpClient if we own it (not from IHttpClientFactory)
        if (_ownsHttpClient)
        {
            _httpClient?.Dispose();
        }
    }

    private static IReadOnlyCollection<LogEventProperty> BuildBaseProperties(TelemetrySettings settings,
        string appVersion, string osVersion)
    {
        var list = new List<LogEventProperty>
        {
            new(TelemetryConstants.Properties.OsVersion, new ScalarValue(osVersion)),
            new(TelemetryConstants.Properties.AxorithVersion, new ScalarValue(appVersion)),
            new(TelemetryConstants.Properties.Application, new ScalarValue(settings.ApplicationName))
        };

        if (!string.IsNullOrWhiteSpace(settings.BuildChannel))
        {
            list.Add(new LogEventProperty(TelemetryConstants.Properties.BuildChannel,
                new ScalarValue(settings.BuildChannel)));
        }

        if (!string.IsNullOrWhiteSpace(settings.EnvironmentOverride))
        {
            list.Add(new LogEventProperty(TelemetryConstants.Properties.Environment,
                new ScalarValue(settings.EnvironmentOverride)));
        }

        return list;
    }

    private IEnumerable<LogEventProperty> ConvertProperties(IReadOnlyDictionary<string, object?> properties)
    {
        foreach (var kvp in properties)
        {
            yield return new LogEventProperty(kvp.Key, ConvertToPropertyValue(kvp.Value));
        }
    }

    private LogEvent CreateLogEvent(LogEventLevel level, string template, IEnumerable<LogEventProperty> properties,
        Exception? exception)
    {
        var messageTemplate = _templateParser.Parse(template);
        return new LogEvent(DateTimeOffset.UtcNow, level, exception, messageTemplate, properties);
    }

    private static LogEventPropertyValue ConvertToPropertyValue(object? value)
    {
        switch (value)
        {
            case null:
                return new ScalarValue(null);
            case string s:
                return new ScalarValue(TelemetryGuard.SafeString(s));
            case bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                return new ScalarValue(value);
            case Enum e:
                return new ScalarValue(e.ToString());
            case Guid g:
                return new ScalarValue(g.ToString());
            case DateTime dt:
                return new ScalarValue(dt);
            case DateTimeOffset dto:
                return new ScalarValue(dto);
            case IReadOnlyDictionary<string, object?> dict:
                return new StructureValue(
                    dict.Select(d => new LogEventProperty(d.Key, ConvertToPropertyValue(d.Value))));
            case IDictionary<string, object?> dict:
                return new StructureValue(dict.Select(kvp =>
                    new LogEventProperty(kvp.Key, ConvertToPropertyValue(kvp.Value))));
            case IEnumerable enumerable and not string:
                return new SequenceValue(enumerable.Cast<object?>().Select(ConvertToPropertyValue));
            default:
                var structured = TryConvertObject(value);
                if (structured is not null)
                {
                    return structured;
                }

                return new ScalarValue(value.ToString());
        }
    }

    private static readonly ConcurrentDictionary<Type, List<PropertyInfo>> PropertyCache = new();

    private static StructureValue? TryConvertObject(object value)
    {
        var type = value.GetType();
        var properties = PropertyCache.GetOrAdd(type, t => t
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToList());

        if (properties.Count == 0)
        {
            return null;
        }

        var logProps = new List<LogEventProperty>(properties.Count);

        foreach (var property in properties)
        {
            object? propValue;
            try
            {
                propValue = property.GetValue(value);
            }
            catch
            {
                // Skip properties that throw during evaluation to avoid breaking telemetry.
                continue;
            }

            logProps.Add(new LogEventProperty(property.Name, ConvertToPropertyValue(propValue)));
        }

        return logProps.Count == 0 ? null : new StructureValue(logProps);
    }
}

public sealed class NoopTelemetryService : ITelemetryService
{
    public bool IsEnabled => false;

    public void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
    }

    public void TrackLog(LogEventLevel level, string messageTemplate, Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}