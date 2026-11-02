using System.Net;
using System.Security.Cryptography;
using Axorith.Client.CoreSdk;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Shared.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class ClientUpdateExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadPersistsVerifiedBytesAndInstallRejectsTamperingOrCancellationBeforeStartingAnything(bool tamper)
    {
        using var endpoint = new InstallerEndpoint();
        using var http = new HttpClient(endpoint);
        using var api = new GrpcUpdatesApi(null!, NullLogger<GrpcUpdatesApi>.Instance, http);
        var update = Update(endpoint.Bytes);

        var path = await api.DownloadUpdateAsync(update);

        Assert.Equal(update.DownloadUrl, Assert.Single(endpoint.Requests));
        Assert.Equal(endpoint.Bytes, await File.ReadAllBytesAsync(path));
        var directory = Path.GetDirectoryName(path)!;
        if (tamper)
        {
            await File.WriteAllTextAsync(path, "changed after download");
            await Assert.ThrowsAsync<InvalidDataException>(() => api.InstallUpdateAsync(path));
        }
        else
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.InstallUpdateAsync(path, cancelled.Token));
        }
        Assert.False(Directory.Exists(directory));
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.InstallUpdateAsync(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedVerificationOrUntrustedUrlCannotProduceAnInstaller(bool untrustedUrl)
    {
        using var endpoint = new InstallerEndpoint();
        using var http = new HttpClient(endpoint);
        using var api = new GrpcUpdatesApi(null!, NullLogger<GrpcUpdatesApi>.Instance, http);
        var update = Update("different release bytes"u8.ToArray());
        if (untrustedUrl)
            update = update with { DownloadUrl = "https://other.example/axorith-setup.exe" };

        await Assert.ThrowsAsync<InvalidDataException>(() => api.DownloadUpdateAsync(update));

        Assert.Equal(untrustedUrl ? 0 : 1, endpoint.Requests.Count);
    }

    private static UpdateInfoDto Update(byte[] bytes) => new("99.1.2",
        UpdateInstallerSource.GetDownloadUri("99.1.2").AbsoluteUri, "Test release", DateTime.UtcNow,
        Convert.ToHexString(SHA256.HashData(bytes)));

    private sealed class InstallerEndpoint : HttpMessageHandler
    {
        public byte[] Bytes { get; } = "installer fixture; never executed"u8.ToArray();
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) });
        }
    }
}
