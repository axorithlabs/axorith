using Axorith.Telemetry;

namespace Axorith.Core.Tests.Services;

[CollectionDefinition("Telemetry environment overrides", DisableParallelization = true)]
public sealed class TelemetryEnvironmentOverrideCollection;

[Collection("Telemetry environment overrides")]
public sealed class TelemetryEnvironmentOverrideTests
{
    [Fact]
    public void DirectTestRunsStartWithTelemetryDisabledAndTestModeEnabled()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("AXORITH_TEST_MODE"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("AXORITH_TELEMETRY_DISABLED"));
        Assert.False(new TelemetrySettings().WithEnvironmentOverrides().IsActive);
    }

    [Theory]
    [InlineData("Axorith.Host", "1", "0")]
    [InlineData("Axorith.Client", "1", "0")]
    [InlineData("Axorith.Host", null, "1")]
    [InlineData("Axorith.Client", null, "1")]
    public async Task DisabledTelemetryNeverCreatesAnHttpClientEvenAfterOptIn(string application,
        string? disabled, string testMode)
    {
        var names = new[] { "AXORITH_TELEMETRY_DISABLED", "AXORITH_TEST_MODE", "AXORITH_TELEMETRY_API_KEY", "AXORITH_TELEMETRY_HOST" };
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(names[0], disabled);
            Environment.SetEnvironmentVariable(names[1], testMode);
            Environment.SetEnvironmentVariable(names[2], "test-project-key");
            Environment.SetEnvironmentVariable(names[3], "http://127.0.0.1:9");
            var factory = new ForbiddenHttpClientFactory();
            await using (var telemetry = new TelemetryService(new TelemetrySettings
                         {
                             Enabled = true, DistinctId = Guid.NewGuid().ToString(), ApplicationName = application
                         }, factory))
            {
                telemetry.SetEnabled(true);
                telemetry.TrackEvent("ApplicationStarted");
                telemetry.TrackEvent("$identify");
                telemetry.TrackError(new InvalidOperationException("fixture"), "test", "test", "error", true, false);
                await telemetry.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
                telemetry.SetEnabled(false);
                telemetry.SetEnabled(true);
                Assert.False(telemetry.IsEnabled);
            }
            Assert.Equal(0, factory.Attempts);
        }
        finally
        {
            for (var i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]);
        }
    }

    private sealed class ForbiddenHttpClientFactory : IHttpClientFactory
    {
        public int Attempts { get; private set; }
        public HttpClient CreateClient(string name)
        {
            Attempts++;
            throw new InvalidOperationException("Tests must not create a PostHog HTTP client.");
        }
    }
}
