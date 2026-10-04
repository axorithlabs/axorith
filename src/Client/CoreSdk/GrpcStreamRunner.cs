using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.CoreSdk;

internal static class GrpcStreamRunner
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    public static async Task RunAsync(Func<CancellationToken, Task> consume, ILogger logger,
        string streamName, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await consume(cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{StreamName} stream error, reconnecting in {DelaySeconds}s...",
                    streamName, ReconnectDelay.TotalSeconds);
                try
                {
                    await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
