using System.Net;
using System.Text.Json;
using Axorith.Host.Services;
using Axorith.Shared.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Axorith.Integrations.Tests;

[Collection("SiteBlocker extension pipes")]
public sealed class UpdateServiceExecutionTests
{
    [ReleaseTheory]
    [InlineData("valid", true)]
    [InlineData("wrong-host", false)]
    [InlineData("wrong-version-url", false)]
    [InlineData("bad-digest", false)]
    [InlineData("missing-installer", false)]
    [InlineData("prerelease", false)]
    [InlineData("invalid-json", false)]
    [InlineData("http-error", false)]
    public async Task RealUpdateCheckAcceptsOnlyPublishedOfficialChecksummedInstallers(string scenario, bool valid)
    {
        const string version = "99.1.2";
        using var endpoint = new ReleaseEndpoint(scenario);
        using var client = new HttpClient(endpoint);
        var previousMode = Environment.GetEnvironmentVariable("AXORITH_TEST_MODE");
        UpdateService service;
        try
        {
            Environment.SetEnvironmentVariable("AXORITH_TEST_MODE", "0");
            service = new UpdateService(NullLogger<UpdateService>.Instance, new ClientFactory(client));
        }
        finally { Environment.SetEnvironmentVariable("AXORITH_TEST_MODE", previousMode); }
        using (service)
        {
            var update = await service.CheckForUpdatesAsync();
            Assert.Equal(UpdateInstallerSource.LatestReleaseUrl, Assert.Single(endpoint.Requests));
            Assert.Equal(valid, service.UpdateAvailable);
            if (valid)
            {
                Assert.NotNull(update);
                Assert.Equal(version, update.Version);
                Assert.Equal(UpdateInstallerSource.GetDownloadUri(version).AbsoluteUri, update.DownloadUrl);
                Assert.Equal(new string('A', 64), update.Sha256Hash);
                Assert.Equal("Release notes", update.ReleaseNotes);
                Assert.Null(service.LastCheckError);
            }
            else
            {
                Assert.Null(update);
                Assert.False(string.IsNullOrWhiteSpace(service.LastCheckError));
            }
        }
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ReleaseEndpoint(string scenario) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            var url = scenario switch
            {
                "wrong-host" => "https://other.example/axorith-setup.exe",
                "wrong-version-url" => UpdateInstallerSource.GetDownloadUri("99.1.1").AbsoluteUri,
                _ => UpdateInstallerSource.GetDownloadUri("99.1.2").AbsoluteUri
            };
            var body = scenario == "invalid-json" ? "broken" : JsonSerializer.Serialize(new
            {
                tag_name = "v99.1.2", draft = false, prerelease = scenario == "prerelease",
                published_at = "2026-10-01T00:00:00Z", body = "Release notes",
                assets = scenario == "missing-installer" ? [] : new[]
                {
                    new { name = "axorith-setup.exe", browser_download_url = url,
                        digest = scenario == "bad-digest" ? "sha256:invalid" : "sha256:" + new string('a', 64) }
                }
            });
            return Task.FromResult(new HttpResponseMessage(scenario == "http-error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(body) });
        }
    }

    private sealed class ReleaseTheoryAttribute : TheoryAttribute
    {
        public ReleaseTheoryAttribute()
        {
#if DEBUG
            Skip = "Update discovery is deliberately disabled in Debug builds; exercise it in Release.";
#endif
        }
    }
}
