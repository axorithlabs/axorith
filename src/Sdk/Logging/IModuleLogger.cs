namespace Axorith.Sdk.Logging;

/// <summary>
///     Defines a simple, abstract logging interface for modules.
///     This decouples modules from any specific logging implementation (e.g., Serilog, NLog).
/// </summary>
public interface IModuleLogger
{
    /// <summary>
    ///     Formats and writes a debug-level log message.
    /// </summary>
    void LogDebug(string messageTemplate, params object[] args);

    /// <summary>
    ///     Formats and writes an informational log message.
    /// </summary>
    void LogInfo(string messageTemplate, params object[] args);

    /// <summary>
    ///     Formats and writes a warning-level log message.
    /// </summary>
    void LogWarning(string messageTemplate, params object[] args);

    /// <summary>
    ///     Formats and writes an error-level log message.
    /// </summary>
    void LogError(Exception? exception, string messageTemplate, params object[] args);

    /// <summary>
    ///     Formats and writes a fatal-level log message.
    ///     Fatal logs are for critical issues that are expected to lead to application termination.
    /// </summary>
    void LogFatal(Exception? exception, string messageTemplate, params object[] args);
}
