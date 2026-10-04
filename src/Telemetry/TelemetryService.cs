using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

    private readonly Logger? _logger;
    private readonly PeriodicBatchingSink? _batchingSink;
    private readonly MessageTemplateParser _templateParser = new();
    private readonly IReadOnlyCollection<LogEventProperty> _baseProperties;
    private readonly HttpClient? _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly object _errorLock = new();
    private readonly object _flushLock = new();
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

    public bool IsEnabled => _logger is not null && Volatile.Read(ref _enabled) != 0 && !_disposed;

    public TelemetryService(TelemetrySettings settings, IHttpClientFactory? httpClientFactory = null)
    {
        var resolved = (settings ?? throw new ArgumentNullException(nameof(settings))).WithEnvironmentOverrides();
        _baseProperties = [];
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

        if (httpClientFactory is not null)
        {
            _httpClient = httpClientFactory.CreateClient(HttpClientName);
        }
        else
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _ownsHttpClient = true;
        }

        var postHogSink = new PostHogSink(
            _httpClient,
            resolved.PostHogApiKey,
            resolved.PostHogHost,
            _distinctId,
            RetryPolicyOptions.FromSettings(resolved),
            () => IsEnabled,
            () => Volatile.Read(ref _preferenceGeneration));
        _batchingSink = new PeriodicBatchingSink(postHogSink, new PeriodicBatchingSinkOptions
        {
            BatchSizeLimit = resolved.BatchSize,
            QueueLimit = resolved.QueueLimit,
            Period = resolved.FlushInterval
        });
        _logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(_batchingSink)
            .CreateLogger();

        SetEnabled(resolved.Enabled);
    }

    public void SetEnabled(bool enabled)
    {
        if (_logger is null || _disposed || Volatile.Read(ref _acceptingEvents) == 0)
        {
            return;
        }

        var wasEnabled = Interlocked.Exchange(ref _enabled, enabled ? 1 : 0) != 0;
        if (wasEnabled != enabled)
        {
            Interlocked.Increment(ref _preferenceGeneration);
        }
        if (!enabled)
        {
            lock (_errorLock)
            {
                foreach (var error in _errors.Values)
                {
                    error.PendingCount = 0;
                    error.PendingFatal = false;
                }
            }

            return;
        }

        if (!wasEnabled || Interlocked.Exchange(ref _identified, 1) == 0)
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
                var logProperties = new List<LogEventProperty>(_baseProperties)
                {
                    new(TelemetryConstants.Properties.EventName, new ScalarValue(name)),
                    new(TelemetryConstants.Properties.PreferenceGeneration,
                        new ScalarValue(Volatile.Read(ref _preferenceGeneration)))
                };
                logProperties.AddRange(safeProperties.Select(pair =>
                    new LogEventProperty(pair.Key, ConvertToPropertyValue(pair.Value))));
                _logger!.Write(new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
                    _templateParser.Parse(name), logProperties));
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
        if (_batchingSink is null || _disposed)
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
                _flushTask = DrainBatchingSinkAsync();
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

    private async Task DrainBatchingSinkAsync()
    {
        try
        {
            await _batchingSink!.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Telemetry is best-effort during shutdown.
        }
        finally
        {
            try { _logger?.Dispose(); }
            catch { }
            if (_ownsHttpClient) _httpClient?.Dispose();
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

    private static IReadOnlyCollection<LogEventProperty> BuildBaseProperties(TelemetrySettings settings,
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
        return TelemetryEventSanitizer.SanitizeProperties(baseValues)
            .Select(pair => new LogEventProperty(pair.Key, ConvertToPropertyValue(pair.Value)))
            .ToArray();
    }


    private static LogEventPropertyValue ConvertToPropertyValue(object? value) => value switch
    {
        null => new ScalarValue(null),
        string text => new ScalarValue(text),
        bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            new ScalarValue(value),
        Guid id => new ScalarValue(id.ToString()),
        IReadOnlyDictionary<string, object?> dictionary => new StructureValue(
            dictionary.Select(pair => new LogEventProperty(pair.Key, ConvertToPropertyValue(pair.Value)))),
        IDictionary<string, object?> dictionary => new StructureValue(
            dictionary.Select(pair => new LogEventProperty(pair.Key, ConvertToPropertyValue(pair.Value)))),
        IEnumerable sequence when value is not string =>
            new SequenceValue(sequence.Cast<object?>().Select(ConvertToPropertyValue)),
        _ => new ScalarValue(null)
    };

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
