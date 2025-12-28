using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;

namespace Axorith.Telemetry;

/// <summary>
///     Interface for PostHog sink to enable testability.
/// </summary>
internal interface IPostHogSink : IBatchedLogEventSink
{
}

/// <summary>
///     Batching sink that sends Serilog events to PostHog /batch endpoint.
///     Includes retry logic with exponential backoff and rate limit handling.
/// </summary>
internal sealed class PostHogSink(
    HttpClient httpClient,
    string apiKey,
    string host,
    string distinctId,
    RetryPolicyOptions? retryOptions = null)
    : IPostHogSink
{
    private static readonly FrozenSet<string> NumericKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "moduleCount",
        "settingsKeyCount",
        "settingsCount",
        "presetNameLength",
        "startDelaySec",
        "uptimeMs",
        "durationMs",
        "sessionDurationMs",
        "activeSessionModuleCount",
        "presetCount",
        "total",
        "count"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly string _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
    private readonly string _host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly string _distinctId = distinctId ?? throw new ArgumentNullException(nameof(distinctId));
    private readonly RetryPolicyOptions _retryOptions = retryOptions ?? new RetryPolicyOptions();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EmitBatchAsync(IEnumerable<LogEvent> batch)
    {
        var events = new List<object>();

        foreach (var logEvent in batch)
        {
            var props = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [TelemetryConstants.Properties.DistinctId] = _distinctId,
                [TelemetryConstants.Properties.Level] = logEvent.Level.ToString(),
                [TelemetryConstants.Properties.Ip] = "0.0.0.0",
                [TelemetryConstants.Properties.IpAlt] = "0.0.0.0",
                [TelemetryConstants.Properties.GeoIpDisable] = true
            };

            foreach (var property in logEvent.Properties)
            {
                if (SensitiveDataMasker.IsSensitiveKey(property.Key))
                {
                    props[property.Key] = SensitiveDataMasker.MaskValue;
                    continue;
                }

                if (SensitiveDataMasker.IsGeoKey(property.Key))
                {
                    continue;
                }

                var simplified = Simplify(property.Key, property.Value);
                if (simplified is null)
                {
                    continue;
                }

                props[property.Key] = simplified;
            }

            if (logEvent.Exception is not null)
            {
                props[TelemetryConstants.Properties.Exception] = TelemetryGuard.SafeStackTrace(logEvent.Exception);
            }

            var name = ResolveEventName(logEvent);

            if (string.Equals(name, TelemetryConstants.IdentifyEvent, StringComparison.OrdinalIgnoreCase))
            {
                var setPayload = ExtractSet(props);
                var identifyProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [TelemetryConstants.Properties.Set] = setPayload,
                    [TelemetryConstants.Properties.Ip] = "0.0.0.0",
                    [TelemetryConstants.Properties.IpAlt] = "0.0.0.0",
                    [TelemetryConstants.Properties.GeoIpDisable] = true
                };

                events.Add(new
                {
                    @event = name,
                    properties = identifyProps,
                    timestamp = logEvent.Timestamp.ToUniversalTime(),
                    distinct_id = _distinctId
                });

                continue;
            }

            events.Add(new
            {
                @event = name,
                properties = props,
                timestamp = logEvent.Timestamp.ToUniversalTime(),
                distinct_id = _distinctId
            });
        }

        if (events.Count == 0)
        {
            return;
        }

        var payload = new
        {
            api_key = _apiKey,
            batch = events
        };

        await SendWithRetryAsync(payload, events.Count).ConfigureAwait(false);
    }

    public Task OnEmptyBatchAsync()
    {
        return Task.CompletedTask;
    }

    private async Task SendWithRetryAsync(object payload, int eventCount)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt <= _retryOptions.MaxRetryAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri());
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", "0.0.0.0");
                request.Headers.TryAddWithoutValidation("CF-Connecting-IP", "0.0.0.0");
                request.Headers.TryAddWithoutValidation("True-Client-IP", "0.0.0.0");
                request.Content = JsonContent.Create(payload, options: _jsonOptions);

                System.Diagnostics.Debug.WriteLine($"PostHog: Sending {eventCount} events to {BuildUri()} (attempt {attempt + 1})");

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"PostHog: Response {(int)response.StatusCode} {response.StatusCode}: {responseBody}");

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    System.Diagnostics.Debug.WriteLine($"PostHog: Rate limited, will retry");
                    var delay = GetRetryAfterDelay(response) ?? _retryOptions.GetDelay(attempt);
                    if (attempt < _retryOptions.MaxRetryAttempts)
                    {
                        await Task.Delay(delay).ConfigureAwait(false);
                        continue;
                    }
                }

                if (IsTransientError(response.StatusCode))
                {
                    System.Diagnostics.Debug.WriteLine($"PostHog: Transient error {response.StatusCode}, will retry");
                    if (attempt < _retryOptions.MaxRetryAttempts)
                    {
                        var delay = _retryOptions.GetDelay(attempt);
                        await Task.Delay(delay).ConfigureAwait(false);
                        continue;
                    }
                }

                System.Diagnostics.Debug.WriteLine(response.IsSuccessStatusCode
                    ? $"PostHog: Successfully sent {eventCount} events"
                    : $"PostHog: Failed with {response.StatusCode}: {responseBody}");

                response.EnsureSuccessStatusCode();
                return;
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"PostHog: HttpRequestException on attempt {attempt + 1}: {ex.Message}");
                lastException = ex;
                if (attempt < _retryOptions.MaxRetryAttempts)
                {
                    var delay = _retryOptions.GetDelay(attempt);
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                System.Diagnostics.Debug.WriteLine($"PostHog: Timeout on attempt {attempt + 1}");
                lastException = ex;
                if (attempt < _retryOptions.MaxRetryAttempts)
                {
                    var delay = _retryOptions.GetDelay(attempt);
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
        }

        if (lastException is not null)
        {
            System.Diagnostics.Debug.WriteLine($"PostHog: All retries exhausted, throwing: {lastException.Message}");
            throw lastException;
        }
    }

    private static bool IsTransientError(HttpStatusCode statusCode)
    {
        return statusCode is >= HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout;
    }

    private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is null)
        {
            return null;
        }

        if (response.Headers.RetryAfter.Delta.HasValue)
        {
            return response.Headers.RetryAfter.Delta.Value;
        }

        if (!response.Headers.RetryAfter.Date.HasValue)
        {
            return null;
        }

        var delay = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);

    }

    private Uri BuildUri()
    {
        var baseUri = _host.EndsWith('/') ? _host : $"{_host}/";
        return new Uri(new Uri(baseUri), "batch");
    }

    private static string ResolveEventName(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue(TelemetryConstants.Properties.EventName, out var eventName) &&
            eventName is ScalarValue { Value: string nameStr } &&
            !string.IsNullOrWhiteSpace(nameStr))
        {
            return nameStr;
        }

        var template = logEvent.MessageTemplate.Text;
        return string.IsNullOrWhiteSpace(template) ? TelemetryConstants.DefaultEvent : template;
    }

    private static object? Simplify(string? key, LogEventPropertyValue value)
    {
        return value switch
        {
            ScalarValue scalar => ScrubScalar(key, scalar.Value),
            SequenceValue sequence => sequence.Elements.Select(v => Simplify(null, v)).ToArray(),
            StructureValue structure => structure.Properties.ToDictionary(p => p.Name, p => Simplify(p.Name, p.Value)),
            DictionaryValue dictionary => dictionary.Elements.ToDictionary(
                kvp => kvp.Key.Value?.ToString() ?? string.Empty,
                kvp => Simplify(kvp.Key.Value?.ToString(), kvp.Value)),
            _ => value.ToString()
        };
    }

    private static object? ScrubScalar(string? key, object? value)
    {
        return value switch
        {
            null => null,
            string s => NormalizeString(key, s),
            bool b => b,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            Guid g => g.ToString(),
            DateTime dt => dt,
            DateTimeOffset dto => dto,
            _ => value.ToString()
        };
    }

    private static object NormalizeString(string? key, string value)
    {
        var keyNonNull = key ?? string.Empty;

        if (SensitiveDataMasker.IsIpMaskExempt(keyNonNull))
        {
            return value;
        }

        if (!NumericKeys.Contains(keyNonNull) ||
            !TryParseNumeric(value, out var numeric))
        {
            return SensitiveDataMasker.MaskIfIpAddress(value);
        }

        return numeric ?? SensitiveDataMasker.MaskIfIpAddress(value);
    }

    private static bool TryParseNumeric(string input, out object? parsed)
    {
        var trimmed = input.Trim();

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            parsed = l;
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture,
                out var d))
        {
            parsed = d;
            return true;
        }

        parsed = null;
        return false;
    }

    private static IReadOnlyDictionary<string, object?> ExtractSet(IReadOnlyDictionary<string, object?> props)
    {
        if (!props.TryGetValue(TelemetryConstants.Properties.Set, out var setObj))
        {
            return new Dictionary<string, object?>();
        }

        return setObj switch
        {
            IReadOnlyDictionary<string, object?> readOnlyDict => readOnlyDict,
            IDictionary<string, object?> dict => new Dictionary<string, object?>(dict),
            _ => new Dictionary<string, object?>()
        };
    }
}