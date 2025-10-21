using Microsoft.Extensions.Logging;

namespace Axorith.Core.Logging;

/// <summary>
/// An adapter that wraps a standard ILogger and exposes it as an IModuleLogger.
/// </summary>
internal class ModuleLoggerAdapter : IModuleLogger
{
    private readonly ILogger _logger;

    public ModuleLoggerAdapter(ILogger logger)
    {
        _logger = logger;
    }

    public void LogDebug(string messageTemplate, params object[] args) => _logger.LogDebug(messageTemplate, args);
    public void LogInfo(string messageTemplate, params object[] args) => _logger.LogInformation(messageTemplate, args);
    public void LogWarning(string messageTemplate, params object[] args) => _logger.LogWarning(messageTemplate, args);
    public void LogError(Exception? exception, string messageTemplate, params object[] args) => _logger.LogError(exception, messageTemplate, args);
    public void LogFatal(Exception? exception, string messageTemplate, params object[] args) => _logger.LogCritical(exception, messageTemplate, args);
}