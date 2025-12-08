using Serilog.Core;
using Serilog.Events;

namespace Axorith.Telemetry;

/// <summary>
/// Serilog sink that forwards log events into ITelemetryService.TrackLog.
/// </summary>
/// <param name="telemetry">The telemetry service to forward logs to. Use NoopTelemetryService if telemetry is disabled.</param>
public sealed class TelemetrySerilogSink(ITelemetryService telemetry) : ILogEventSink
{
    private readonly ITelemetryService _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));

    public void Emit(LogEvent logEvent)
    {
        if (!_telemetry.IsEnabled)
        {
            return;
        }

        var props = ConvertProperties(logEvent.Properties);
        _telemetry.TrackLog(logEvent.Level, logEvent.MessageTemplate.Text, logEvent.Exception, props);
    }

    private static IReadOnlyDictionary<string, object?> ConvertProperties(IReadOnlyDictionary<string, LogEventPropertyValue> properties)
    {
        var dict = new Dictionary<string, object?>(properties.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in properties)
        {
            dict[kvp.Key] = ToClr(kvp.Value);
        }

        return dict;
    }

    private static object? ToClr(LogEventPropertyValue value)
    {
        return value switch
        {
            ScalarValue scalar => scalar.Value,
            SequenceValue sequence => sequence.Elements.Select(ToClr).ToArray(),
            StructureValue structure => structure.Properties.ToDictionary(p => p.Name, p => ToClr(p.Value),
                StringComparer.OrdinalIgnoreCase),
            DictionaryValue dictionary => dictionary.Elements.ToDictionary(
                kvp => kvp.Key.Value?.ToString() ?? string.Empty, kvp => ToClr(kvp.Value),
                StringComparer.OrdinalIgnoreCase),
            _ => value.ToString()
        };
    }
}
