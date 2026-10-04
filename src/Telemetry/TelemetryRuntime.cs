using Serilog;

namespace Axorith.Telemetry;

public static class TelemetryRuntime
{
    public static void LogConfiguration(string component, TelemetrySettings settings, bool isEnabled)
    {
        Log.Information(
            "Telemetry ({Component}): enabled={Enabled}, active={Active}, isEnabled={IsEnabled}, host={Host}, batch={Batch}, queue={Queue}, flushSec={Flush}",
            component, settings.Enabled, settings.IsActive, isEnabled, settings.PostHogHost, settings.BatchSize,
            settings.QueueLimit, settings.FlushInterval.TotalSeconds);

        if (!settings.IsActive)
        {
            Log.Warning(
                "Telemetry is INACTIVE. Reasons: Enabled={Enabled}, ApiKeyIsPlaceholder={IsPlaceholder}, ApiKeyEmpty={IsEmpty}, HostEmpty={HostEmpty}",
                settings.Enabled,
                !string.IsNullOrWhiteSpace(settings.PostHogApiKey) &&
                settings.PostHogApiKey.StartsWith("##", StringComparison.Ordinal),
                string.IsNullOrWhiteSpace(settings.PostHogApiKey),
                string.IsNullOrWhiteSpace(settings.PostHogHost));
        }
    }

    public static void RegisterGlobalExceptionHandlers(ITelemetryService? telemetry, string subsystem)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            if (e.IsTerminating)
            {
                Log.Fatal(exception, "Unhandled exception in AppDomain (terminating)");
                if (exception is not null)
                    telemetry?.TrackError(exception, subsystem, "startup", "fatal", handled: false, fatal: true);
                TryFlush(telemetry);
            }
            else
            {
                Log.Error(exception, "Unhandled exception in AppDomain (non-terminating)");
                if (exception is not null)
                    telemetry?.TrackError(exception, subsystem, "unknown", "error", handled: false, fatal: false);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
            telemetry?.TrackError(e.Exception, subsystem, "unknown", "warning", handled: true, fatal: false);
        };
    }

    private static void TryFlush(ITelemetryService? telemetry)
    {
        if (telemetry is null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            telemetry.FlushAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort during process termination.
        }
    }
}
