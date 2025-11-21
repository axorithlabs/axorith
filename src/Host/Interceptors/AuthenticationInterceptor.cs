using Axorith.Contracts;
using Axorith.Host.Services;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Axorith.Host.Interceptors;

/// <summary>
///     Global gRPC interceptor that enforces token-based authentication for all requests.
/// </summary>
public class AuthenticationInterceptor(
    IHostAuthenticationService authService,
    ILogger<AuthenticationInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return await continuation(request, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        return await continuation(requestStream, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        await continuation(request, responseStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authorize(context);
        await continuation(requestStream, responseStream, context);
    }

    private void Authorize(ServerCallContext context)
    {
        var token = context.RequestHeaders.GetValue(AuthConstants.TokenHeaderName);

        if (authService.ValidateToken(token))
        {
            return;
        }

        logger.LogWarning("Unauthorized access attempt from {Peer}. Token present: {HasToken}",
            context.Peer, !string.IsNullOrEmpty(token));

        throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid or missing authentication token."));
    }
}