namespace Axorith.Sdk.Logging;

/// <summary>
/// Defines a simple, abstract logging interface for modules.
/// This decouples modules from any specific logging implementation (e.g., Serilog, NLog).
/// </summary>
public interface IModuleLogger
{
    /// <summary>
    /// Formats and writes a debug-level log message.
    /// </summary>
    /// <param name="messageTemplate">The message template, e.g., "Processing item {ItemId}".</param>
    /// <param name="args">Optional arguments for the placeholders in the template.</param>
    void LogDebug(string messageTemplate, params object[] args);

    /// <summary>
    /// Formats and writes an informational log message.
    /// </summary>
    /// <param name="messageTemplate">The message template, e.g., "Session started for user {UserName}".</param>
    /// <param name="args">Optional arguments for the placeholders in the template.</param>
    void LogInfo(string messageTemplate, params object[] args);

    /// <summary>
    /// Formats and writes a warning-level log message.
    /// </summary>
    /// <param name="messageTemplate">The message template, e.g., "API call to {Endpoint} timed out".</param>
    /// <param name="args">Optional arguments for the placeholders in the template.</param>
    void LogWarning(string messageTemplate, params object[] args);

    /// <summary>
    /// Formats and writes an error-level log message.
    /// </summary>
    /// <param name="exception">The exception to log. This is the conventional first parameter.</param>
    /// <param name="messageTemplate">The message template, e.g., "Failed to process item {ItemId}".</param>
    /// <param name="args">Optional arguments for the placeholders in the template.</param>
    void LogError(Exception? exception, string messageTemplate, params object[] args);

    /// <summary>
    /// Formats and writes a fatal-level log message.
    /// Fatal logs are for critical issues that are expected to lead to application termination.
    /// </summary>
    /// <param name="exception">The exception to log.</param>
    /// <param name="messageTemplate">The message template, e.g., "Could not connect to database at {Host}".</param>
    /// <param name="args">Optional arguments for the placeholders in the template.</param>
    void LogFatal(Exception? exception, string messageTemplate, params object[] args);
}