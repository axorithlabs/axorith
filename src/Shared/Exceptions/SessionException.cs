namespace Axorith.Shared.Exceptions;

public class SessionException(string message, Exception? innerException = null) : Exception(message, innerException);
