using System.Text.Json;
using Axorith.Telemetry;

namespace Axorith.Core.Tests.Services;

public sealed class LegacyTelemetryIdentityTests
{
    [Theory]
    [InlineData("registration.json")]
    [InlineData("installation-id.txt")]
    public void MigrationRemovesObsoleteIdentityStorageOnce(string fileName)
    {
        var directory = Directory.CreateTempSubdirectory("axorith-legacy-identity-");
        try
        {
            var path = Path.Combine(directory.FullName, fileName);
            var oldId = Guid.NewGuid();
            File.WriteAllText(path, fileName.EndsWith(".json", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { machineId = oldId }) : oldId.ToString());
            var telemetry = new MigrationRecorder();
            LegacyTelemetryIdentity.Migrate(telemetry, Guid.NewGuid().ToString(), path);
            Assert.Empty(Directory.GetFiles(directory.FullName));
            LegacyTelemetryIdentity.Migrate(telemetry, Guid.NewGuid().ToString(), path);
            Assert.Equal(1, telemetry.Migrations);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class MigrationRecorder : ITelemetryService
    {
        public int Migrations { get; private set; }
        public bool IsEnabled => true;
        public void SetEnabled(bool enabled) { }
        public void TrackEvent(string eventName, IReadOnlyDictionary<string, object?>? properties = null) => Migrations++;
        public void TrackError(Exception exception, string subsystem, string operation, string severity,
            bool handled, bool fatal, IReadOnlyDictionary<string, object?>? properties = null) { }
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
