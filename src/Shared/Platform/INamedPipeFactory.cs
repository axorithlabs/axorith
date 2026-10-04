using System.IO.Pipes;

namespace Axorith.Shared.Platform;

public interface INamedPipeFactory
{
    NamedPipeServerStream CreateSecureServerPipe(
        string pipeName,
        PipeDirection direction = PipeDirection.In,
        int maxNumberOfServerInstances = 1);
}
