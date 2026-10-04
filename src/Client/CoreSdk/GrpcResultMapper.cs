using OperationResult = Axorith.Client.CoreSdk.Abstractions.OperationResult;

namespace Axorith.Client.CoreSdk;

internal static class GrpcResultMapper
{
    public static OperationResult ToModel(Axorith.Contracts.OperationResult response) =>
        new(response.Success, response.Message,
            response.Errors.Count > 0 ? response.Errors.ToList() : null,
            response.Warnings.Count > 0 ? response.Warnings.ToList() : null);
}
