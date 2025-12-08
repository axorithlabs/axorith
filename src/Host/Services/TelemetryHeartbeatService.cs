using System.Diagnostics;
using Axorith.Core.Services.Abstractions;
using Axorith.Telemetry;

namespace Axorith.Host.Services;

internal sealed class TelemetryHeartbeatService(
    ITelemetryService telemetry,
    IPresetManager presetManager,
    ISessionManager sessionManager,
    IModuleRegistry moduleRegistry,
    ILogger<TelemetryHeartbeatService> logger,
    Stopwatch hostUptime)
    : BackgroundService
{
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(60));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _timer.WaitForNextTickAsync(stoppingToken);
                await SendHeartbeatAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heartbeat tick failed");
            }
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        if (!telemetry.IsEnabled)
        {
            return;
        }

        try
        {
            var presets = await presetManager.LoadAllPresetsAsync(ct).ConfigureAwait(false);
            var presetCount = presets.Count;
            var allModules = presets.SelectMany(p => p.Modules).ToList();
            var moduleCount = allModules.Count;
            var distinctModuleIds = allModules.Select(m => m.ModuleId.ToString()).Distinct().Take(64).ToArray();

            var snapshot = sessionManager.GetCurrentSnapshot();
            var activeSessionId = snapshot?.PresetId.ToString();
            var activeModuleCount = snapshot?.Modules.Count ?? 0;

            var allModuleDefs = moduleRegistry.GetAllDefinitions();
            var moduleDefLookup = allModuleDefs.ToDictionary(m => m.Id, m => m.Name);

            var presetData = presets.Select(p => new Dictionary<string, object?>
            {
                ["id"] = p.Id.ToString(),
                ["name"] = TelemetryGuard.SafeString(p.Name),
                ["version"] = p.Version,
                ["moduleCount"] = p.Modules.Count,
                ["modules"] = p.Modules.Select(m =>
                {
                    var moduleName = moduleDefLookup.TryGetValue(m.ModuleId, out var name) ? name : "Unknown";
                    return new Dictionary<string, object?>
                    {
                        ["instanceId"] = m.InstanceId.ToString(),
                        ["moduleId"] = m.ModuleId.ToString(),
                        ["moduleName"] = TelemetryGuard.SafeString(moduleName),
                        ["customName"] = TelemetryGuard.SafeString(m.CustomName),
                        ["startDelayMs"] = (long)m.StartDelay.TotalMilliseconds,
                        ["settingsCount"] = m.Settings.Count,
                        ["settingKeys"] = m.Settings.Keys.Take(32).ToArray()
                    };
                }).ToArray()
            }).ToArray();

            telemetry.TrackEvent("HostHeartbeat", new Dictionary<string, object?>
            {
                ["uptimeMs"] = (long)hostUptime.Elapsed.TotalMilliseconds,
                ["presetCount"] = presetCount,
                ["moduleCount"] = moduleCount,
                ["moduleIds"] = distinctModuleIds,
                ["activeSession"] = snapshot != null,
                ["activeSessionId"] = activeSessionId,
                ["activeSessionModuleCount"] = activeModuleCount,
                ["presets"] = presetData
            });

            logger.LogDebug("Telemetry heartbeat sent. Uptime: {Uptime}ms", hostUptime.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to produce telemetry heartbeat");
        }
    }

    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }
}