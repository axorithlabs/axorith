using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class PendingInstallationTelemetryTests
{
    [Fact]
    public void PendingInstallationRequiresTelemetryConsentAndTheSameInstallationId()
    {
        var installationId = Guid.NewGuid();

        Assert.True(Axorith.Client.Program.IsPendingInstallationEligible(true, installationId,
            installationId.ToString("D")));
        Assert.False(Axorith.Client.Program.IsPendingInstallationEligible(false, installationId,
            installationId.ToString("D")));
        Assert.False(Axorith.Client.Program.IsPendingInstallationEligible(true, installationId,
            Guid.NewGuid().ToString("D")));
        Assert.False(Axorith.Client.Program.IsPendingInstallationEligible(true, installationId, "invalid"));
    }
}
