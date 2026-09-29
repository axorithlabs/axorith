using System.Text.Json;
using Axorith.Core.Tests.Helpers;
using Axorith.Telemetry;

namespace Axorith.Core.Tests.Services;

public sealed class TelemetryServiceTests
{
    [Fact]
    public async Task FlushAsync_SendsQueuedEventsAndKeepsWhitelistedProperties()
    {
        await using var server = new PostHogTestServer();
        var distinctId = Guid.NewGuid().ToString("D");
        await using var telemetry = new TelemetryService(new TelemetrySettings
        {
            Enabled = true,
            PostHogApiKey = "test-project-key",
            PostHogHost = server.Host,
            DistinctId = distinctId,
            ApplicationName = "Axorith.Client",
            AppVersion = "1.2.3",
            OsVersion = "Windows 11",
            BatchSize = 100,
            FlushInterval = TimeSpan.FromHours(1)
        });

        var installAttemptId = Guid.NewGuid();
        telemetry.TrackEvent("InstallationConfirmed", new Dictionary<string, object?>
        {
            ["installAttemptId"] = installAttemptId,
            ["installMode"] = "update",
            ["currentVersion"] = "1.2.3",
            ["previousVersion"] = "1.2.2",
            ["privateValue"] = "must be removed"
        });

        using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await telemetry.FlushAsync(flushCts.Token);

        var sentEvent = server.Payloads
            .SelectMany(body =>
            {
                using var payload = JsonDocument.Parse(body);
                return payload.RootElement.GetProperty("batch").EnumerateArray()
                    .Select(item => item.Clone()).ToArray();
            })
            .Single(item => item.GetProperty("event").GetString() == "InstallationConfirmed");
        var properties = sentEvent.GetProperty("properties");

        Assert.Equal(distinctId, sentEvent.GetProperty("distinct_id").GetString());
        Assert.Equal(installAttemptId.ToString("D"), properties.GetProperty("installAttemptId").GetString());
        Assert.Equal("update", properties.GetProperty("installMode").GetString());
        Assert.Equal("1.2.3", properties.GetProperty("currentVersion").GetString());
        Assert.Equal("1.2.2", properties.GetProperty("previousVersion").GetString());
        Assert.False(properties.TryGetProperty("privateValue", out _));
    }
}
