using System.IO.Pipes;

namespace Axorith.Shared.Platform;

/// <summary>
///     Provides platform-specific named pipe creation with appropriate security settings.
/// </summary>
public interface INamedPipeFactory
{
    /// <summary>
    ///     Creates a secure named pipe server stream.
    ///     On Windows: Creates pipe with ACL restricting access to current user only.
    ///     On other platforms: Creates standard pipe (relies on OS-level permissions).
    /// </summary>
    /// <param name="pipeName">Name of the pipe.</param>
    /// <param name="direction">Direction of the pipe.</param>
    /// <param name="maxNumberOfServerInstances">Maximum number of server instances.</param>
    /// <returns>Configured NamedPipeServerStream.</returns>
    NamedPipeServerStream CreateSecureServerPipe(
        string pipeName, 
        PipeDirection direction = PipeDirection.In,
        int maxNumberOfServerInstances = 1);
}
