using Microsoft.Extensions.Logging;

namespace Axorith.Core.Logging;

/// <summary>
///     An adapter that wraps a standard ILogger and exposes it as an IModuleLogger.
/// </summary>
internal class ModuleLoggerAdapter(ILogger logger) : IModuleLogger
{
    public void LogDebug(string messageTemplate, params object[] args)
    {
        logger.LogDebug(messageTemplate, args);
    }

    public void LogInfo(string messageTemplate, params object[] args)
    {
        logger.LogInformation(messageTemplate, args);
    }

    public void LogWarning(string messageTemplate, params object[] args)
    {
        logger.LogWarning(messageTemplate, args);
    }

    public void LogError(Exception? exception, string messageTemplate, params object[] args)
    {
        logger.LogError(exception, messageTemplate, args);
    }

    public void LogFatal(Exception? exception, string messageTemplate, params object[] args)
    {
        logger.LogCritical(exception, messageTemplate, args);
    }
}