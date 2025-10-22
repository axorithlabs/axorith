using Serilog.Core;
using Serilog.Events;

namespace Axorith.Core.Logging;

/// <summary>
///     Enriches log events with a formatted module context string, e.g., "[ModuleName]" or "[ModuleName | InstanceName]".
/// </summary>
public class ModuleContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var hasModuleName = logEvent.Properties.TryGetValue("ModuleName", out var moduleNameValue) &&
                            moduleNameValue is ScalarValue { Value: string moduleName } &&
                            !string.IsNullOrWhiteSpace(moduleName);

        var hasInstanceName = logEvent.Properties.TryGetValue("ModuleInstanceName", out var instanceNameValue) &&
                              instanceNameValue is ScalarValue { Value: string instanceName } &&
                              !string.IsNullOrWhiteSpace(instanceName);

        var formattedContext = string.Empty;

        if (hasModuleName)
        {
            var moduleNameStr = (moduleNameValue as ScalarValue)!.Value!.ToString();

            if (hasInstanceName)
            {
                var instanceNameStr = (instanceNameValue as ScalarValue)!.Value!.ToString();

                formattedContext = !string.Equals(moduleNameStr, instanceNameStr, StringComparison.OrdinalIgnoreCase)
                    ? $"[{moduleNameStr} | {instanceNameStr}] "
                    : $"[{moduleNameStr}] ";
            }
            else
            {
                formattedContext = $"[{moduleNameStr}] ";
            }
        }

        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("ModuleContext", formattedContext));
    }
}