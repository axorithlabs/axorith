using Serilog.Core;
using Serilog.Events;

namespace Axorith.Core.Logging;

public class ShortSourceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!logEvent.Properties.TryGetValue("SourceContext", out var sourceContext)) return;
        
        var fullSourceName = sourceContext.ToString().Trim('"');
        var shortSourceName = fullSourceName.Split('.').LastOrDefault() ?? fullSourceName;
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("ShortSourceContext", shortSourceName));
    }
}