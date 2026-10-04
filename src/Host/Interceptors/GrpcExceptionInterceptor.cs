using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Axorith.Host.Interceptors;

public sealed class GrpcExceptionInterceptor(ILogger<GrpcExceptionInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled gRPC error in {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error.", ex));
        }
    }
}
