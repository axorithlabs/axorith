using Axorith.Sdk.Logging;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2254

namespace Axorith.Core.Logging;

/// <summary>
///     An adapter that wraps a standard ILogger and exposes it as an IModuleLogger.
/// </summary>
internal class ModuleLoggerAdapter(ILogger logger, string moduleName) : IModuleLogger
{
    private IDisposable BeginModuleScope()
    {
        return logger.BeginScope(new Dictionary<string, object>
        {
            ["ModuleName"] = Sanitize(moduleName)
        })!;
    }

    public void LogDebug(string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogDebug(Sanitize(messageTemplate), SanitizeArguments(args));
    }

    public void LogInfo(string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogInformation(Sanitize(messageTemplate), SanitizeArguments(args));
    }

    public void LogWarning(string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogWarning(Sanitize(messageTemplate), SanitizeArguments(args));
    }

    public void LogError(Exception? exception, string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogError(exception, Sanitize(messageTemplate), SanitizeArguments(args));
    }

    public void LogFatal(Exception? exception, string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogCritical(exception, Sanitize(messageTemplate), SanitizeArguments(args));
    }

    private static string Sanitize(string value)
    {
        return value.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static object[] SanitizeArguments(object[] args)
    {
        return Array.ConvertAll(args, arg => arg is string value ? Sanitize(value) : arg);
    }
}
