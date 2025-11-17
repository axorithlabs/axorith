using Axorith.Sdk.Logging;
using Microsoft.Extensions.Logging;

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
            ["ModuleName"] = moduleName
        })!;
    }

    public void LogDebug(string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogDebug(messageTemplate, args);
    }

    public void LogInfo(string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogInformation(messageTemplate, args);
    }

    public void LogWarning(string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogWarning(messageTemplate, args);
    }

    public void LogError(Exception? exception, string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogError(exception, messageTemplate, args);
    }

    public void LogFatal(Exception? exception, string messageTemplate, params object[] args)
    {
        using var scope = BeginModuleScope();
        logger.LogCritical(exception, messageTemplate, args);
    }
}