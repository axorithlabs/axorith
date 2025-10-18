namespace Axorith.Shared.Exceptions;

/// <summary>
/// Thrown for errors related to session management, such as starting a session when one is already active.
/// </summary>
public class SessionException(string message) : AxorithException(message);