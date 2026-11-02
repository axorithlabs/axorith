using Axorith.Sdk.Logging;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2254

namespace Axorith.Core.Logging;

internal class ModuleLoggerAdapter(ILogger logger, string moduleName) : IModuleLogger
{
    public void LogDebug(string messageTemplate, params object[] args) =>
        Log(LogLevel.Debug, null, messageTemplate, args);

    public void LogInfo(string messageTemplate, params object[] args) =>
        Log(LogLevel.Information, null, messageTemplate, args);

    public void LogWarning(string messageTemplate, params object[] args) =>
        Log(LogLevel.Warning, null, messageTemplate, args);

    public void LogError(Exception? exception, string messageTemplate, params object[] args) =>
        Log(LogLevel.Error, exception, messageTemplate, args);

    public void LogFatal(Exception? exception, string messageTemplate, params object[] args) =>
        Log(LogLevel.Critical, exception, messageTemplate, args);

    private void Log(LogLevel level, Exception? exception, string messageTemplate, object[] args)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["ModuleName"] = Sanitize(moduleName) });
        logger.Log(level, exception, Sanitize(messageTemplate), SanitizeArguments(args));
    }

    private static string Sanitize(string value) => value.Replace("\r", "\\r").Replace("\n", "\\n");

    private static object[] SanitizeArguments(object[] args) => Array.ConvertAll(args, arg => arg is string value ? Sanitize(value) : arg);
}
