using System.Diagnostics;
using System.Text.Json;

namespace Axorith.Telemetry;

internal static class LegacyTelemetryIdentity
{
    internal static void Migrate(ITelemetryService telemetry, string newId, params string[] legacyFiles)
    {
        if (!telemetry.IsEnabled) return;

        var migrated = new HashSet<Guid>();
        foreach (var path in legacyFiles)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var value = File.ReadAllText(path).Trim();
                if (Path.GetExtension(path) == ".json")
                {
                    using var document = JsonDocument.Parse(value);
                    var root = document.RootElement;
                    value = root.TryGetProperty("machineId", out var id) ||
                            root.TryGetProperty("installationId", out id)
                        ? id.GetString() : null;
                }

                if (Guid.TryParse(value, out var oldId) && migrated.Add(oldId))
                {
                    telemetry.TrackEvent(TelemetryConstants.IdentityMigratedEvent, new Dictionary<string, object?>
                    {
                        ["oldId"] = oldId,
                        ["newId"] = newId
                    });
                }

                // One-shot migration: remove obsolete identity storage without pending/sent state.
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                Debug.WriteLine($"Legacy telemetry identity migration failed: {ex.GetType().Name}");
            }
        }
    }
}
