using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.Configuration;
using ReactiveUI.Avalonia;
using Serilog;
using Axorith.Telemetry;

namespace Axorith.Client;

internal static class Program
{
    internal static ITelemetryService? Telemetry { get; private set; }
    private static readonly Stopwatch AppUptime = Stopwatch.StartNew();

    [STAThread]
    public static int Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var telemetrySettings = (configuration.GetSection("Telemetry").Get<TelemetrySettings>() ?? new TelemetrySettings())
            .WithEnvironmentOverrides() with { ApplicationName = "Axorith.Client" };

        Telemetry = new TelemetryService(telemetrySettings);
        var telemetryLogLevel = TelemetrySettings.ResolveLogLevel(telemetrySettings.LogLevel);
        
        Log.Information("Telemetry (Client): enabled={Enabled}, active={Active}, isEnabled={IsEnabled}, host={Host}, batch={Batch}, queue={Queue}, flushSec={FlushSec}",
            telemetrySettings.Enabled,
            telemetrySettings.IsActive,
            Telemetry?.IsEnabled ?? false,
            telemetrySettings.PostHogHost,
            telemetrySettings.BatchSize,
            telemetrySettings.QueueLimit,
            telemetrySettings.FlushInterval.TotalSeconds);

        if (!telemetrySettings.IsActive)
        {
            Log.Warning("Telemetry is INACTIVE. Reasons: Enabled={Enabled}, ApiKeyIsPlaceholder={IsPlaceholder}, ApiKeyEmpty={IsEmpty}, HostEmpty={HostEmpty}",
                telemetrySettings.Enabled,
                telemetrySettings.PostHogApiKey.StartsWith("##", StringComparison.Ordinal),
                string.IsNullOrWhiteSpace(telemetrySettings.PostHogApiKey),
                string.IsNullOrWhiteSpace(telemetrySettings.PostHogHost));
            Log.Information("To enable telemetry, set AXORITH_TELEMETRY_API_KEY environment variable or update appsettings.json");
        }
        using var heartbeatCts = new CancellationTokenSource();
        Task? heartbeatTask = null;

        var logsPath = configuration.GetValue<string>("Serilog:WriteTo:1:Args:path")
                       ?? "%AppData%/Axorith/logs/client-.log";
        var resolvedLogsPath = Environment.ExpandEnvironmentVariables(logsPath);
        var logsDir = Path.GetDirectoryName(resolvedLogsPath);
        if (!string.IsNullOrEmpty(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "Axorith.Client")
            .WriteTo.Sink(new TelemetrySerilogSink(Telemetry ?? new NoopTelemetryService()), restrictedToMinimumLevel: telemetryLogLevel)
            .CreateLogger();

        try
        {
            Log.Information("Axorith Client starting");
            Log.Information("Version: {Version}, OS: {OS}", 
                typeof(Program).Assembly.GetName().Version, 
                Environment.OSVersion);

            Telemetry?.TrackEvent("AppStarted");
            heartbeatTask = RunHeartbeatAsync(heartbeatCts.Token);

            var app = BuildAvaloniaApp();

            RegisterGlobalExceptionHandlers();

            // Tray icon is always visible, but --tray hides window on startup
            // Use OnExplicitShutdown to prevent closing app when window is closed
            app.StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

            Log.Information("Axorith Client shut down gracefully");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Axorith Client terminated unexpectedly");

            return 1;
        }
        finally
        {
            Log.CloseAndFlush();

            Telemetry?.TrackEvent("AppUptime", new Dictionary<string, object?>
            {
                ["durationMs"] = (long)AppUptime.Elapsed.TotalMilliseconds
            });

            heartbeatCts.Cancel();
            heartbeatTask?.GetAwaiter().GetResult();
            
            using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Telemetry?.FlushAsync(flushCts.Token).GetAwaiter().GetResult();
            Telemetry?.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI()
            .AfterSetup(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    Log.Error(e.Exception, "Unhandled exception in UI thread");
                    // Don't mark as handled - let Avalonia decide whether to crash or not
                };
            });
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        // Catch unhandled exceptions in any AppDomain thread
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            if (e.IsTerminating)
            {
                Log.Fatal(exception, "Unhandled exception in AppDomain (terminating)");
                Telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
                {
                    ["fatal"] = true,
                    ["message"] = TelemetryGuard.SafeString(exception?.Message),
                    ["stack"] = TelemetryGuard.SafeStackTrace(exception)
                });
                Telemetry?.FlushAsync().GetAwaiter().GetResult();
            }
            else
            {
                Log.Error(exception, "Unhandled exception in AppDomain (non-terminating)");
                Telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
                {
                    ["fatal"] = false,
                    ["message"] = TelemetryGuard.SafeString(exception?.Message),
                    ["stack"] = TelemetryGuard.SafeStackTrace(exception)
                });
            }
        };

        // Catch unobserved task exceptions
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved(); // Prevent process termination
            Telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
            {
                ["fatal"] = false,
                ["message"] = TelemetryGuard.SafeString(e.Exception?.Message),
                ["stack"] = TelemetryGuard.SafeStackTrace(e.Exception)
            });
        };

        Log.Debug("Global exception handlers registered");
    }

    private static async Task RunHeartbeatAsync(CancellationToken ct)
    {
        if (Telemetry is not { IsEnabled: true })
        {
            return;
        }

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                Telemetry.TrackEvent("ClientHeartbeat", new Dictionary<string, object?>
                {
                    ["uptimeMs"] = (long)AppUptime.Elapsed.TotalMilliseconds
                });
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        finally
        {
            timer.Dispose();
        }
    }
}