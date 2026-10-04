using Grpc.Core;
using Grpc.Core.Testing;

namespace Axorith.Host.Tests.Services;

internal static class GrpcTestContext
{
    public static ServerCallContext Create(string method = "test") => TestServerCallContext.Create(
        method,
        "localhost",
        DateTime.UtcNow.AddMinutes(5),
        [],
        CancellationToken.None,
        "127.0.0.1",
        null,
        null,
        _ => Task.CompletedTask,
        () => new WriteOptions(),
        _ => { });
}
