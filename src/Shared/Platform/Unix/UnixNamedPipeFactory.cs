using System.IO.Pipes;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Unix;

[UnsupportedOSPlatform("windows")]
internal class UnixNamedPipeFactory(ILogger<UnixNamedPipeFactory> logger) : INamedPipeFactory
{
    public NamedPipeServerStream CreateSecureServerPipe(
        string pipeName,
        PipeDirection direction = PipeDirection.In,
        int maxNumberOfServerInstances = 1)
    {
        logger.LogDebug("Created named pipe: {PipeName}", pipeName);

        return new NamedPipeServerStream(
            pipeName,
            direction,
            maxNumberOfServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }
}