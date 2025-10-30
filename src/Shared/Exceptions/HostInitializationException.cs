namespace Axorith.Shared.Exceptions;

/// <summary>
///     Thrown when the AxorithHost fails to initialize, often due to a critical error during startup.
/// </summary>
public class HostInitializationException(string message, Exception innerException)
    : AxorithException(message, innerException);