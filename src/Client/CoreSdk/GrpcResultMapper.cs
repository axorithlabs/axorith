using Grpc.Core;
using Polly.Retry;
using OperationResult = Axorith.Client.CoreSdk.Abstractions.OperationResult;

namespace Axorith.Client.CoreSdk;

internal static class GrpcResultMapper
{
    public static Task<OperationResult> ExecuteAsync(AsyncRetryPolicy retryPolicy,
        Func<AsyncUnaryCall<Axorith.Contracts.OperationResult>> request) => ExecuteAsync(retryPolicy, request, ToModel);

    public static Task<TModel> ExecuteAsync<TResponse, TModel>(AsyncRetryPolicy retryPolicy,
        Func<AsyncUnaryCall<TResponse>> request, Func<TResponse, TModel> toModel) => retryPolicy.ExecuteAsync(async () =>
    {
        using var call = request();
        return toModel(await call.ResponseAsync.ConfigureAwait(false));
    });

    public static OperationResult ToModel(Axorith.Contracts.OperationResult response) =>
        new(response.Success, response.Message,
            response.Errors.Count > 0 ? response.Errors.ToList() : null,
            response.Warnings.Count > 0 ? response.Warnings.ToList() : null);
}
