namespace Axorith.Shared.Exceptions;

/// <summary>
/// Base class for all custom exceptions in the Axorith application.
/// </summary>
public abstract class AxorithException : Exception
{
    protected AxorithException(string message) : base(message)
    {
    }

    protected AxorithException(string message, Exception innerException) : base(message, innerException)
    {
    }
}