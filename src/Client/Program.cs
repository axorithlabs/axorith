using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Axorith.Client.Services;
using Axorith.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ReactiveUI.Avalonia;
using Serilog;
using Serilog.Extensions.Logging;

namespace Axorith.Client;

internal static class Program
{
    internal static ITelemetryService? Telemetry { get; private set; }
    private static readonly Stopwatch AppUptime = Stopwatch.StartNew();
    private static SingleInstanceManager? _singleInstanceManager;

    [STAThread]
    public static int Main(string[] args)
    {
        // Early logging setup for single instance check
        var earlyLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        var earlyLoggerFactory = new SerilogLoggerFactory(earlyLogger);
        var singleInstanceLogger = earlyLoggerFactory.CreateLogger<SingleInstanceManager>();

        // Check for single instance BEFORE any heavy initialization
        _singleInstanceManager = new SingleInstanceManager(singleInstanceLogger);
        
        if (!_singleInstanceManager.TryAcquireLock())
        {
            earlyLogger.Information("Another instance detected - sending activation request");
            
            // Send activation request to existing instance
            var activationTask = _singleInstanceManager.SendActivationRequestAsync();
            activationTask.Wait(TimeSpan.FromSeconds(5));
            
            if (activationTask.Result)
            {
                earlyLogger.Information("Activation request sent successfully - exiting");
            }
            else
            {
                earlyLogger.Warning("Failed to activate existing instance - exiting anyway");
            }
            
            _singleInstanceManager.Dispose();
            earlyLogger.Dispose();
            return 0;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var telemetryEnabled = LoadTelemetryEnabledSetting();

        var telemetrySettings = new TelemetrySettings()
                .WithEnvironmentOverrides() with
            {
                ApplicationName = "Axorith.Client",
                Enabled = telemetryEnabled
            };

        Telemetry = new TelemetryService(telemetrySettings);
        var telemetryLogLevel = TelemetrySettings.ResolveLogLevel(telemetrySettings.LogLevel);

        Log.Information(
            "Telemetry (Client): enabled={Enabled}, active={Active}, isEnabled={IsEnabled}, host={Host}, batch={Batch}, queue={Queue}, flushSec={FlushSec}",
            telemetrySettings.Enabled,
            telemetrySettings.IsActive,
            Telemetry.IsEnabled,
            telemetrySettings.PostHogHost,
            telemetrySettings.BatchSize,
            telemetrySettings.QueueLimit,
            telemetrySettings.FlushInterval.TotalSeconds);

        if (!telemetrySettings.IsActive)
        {
            Log.Warning(
                "Telemetry is INACTIVE. Reasons: Enabled={Enabled}, ApiKeyIsPlaceholder={IsPlaceholder}, ApiKeyEmpty={IsEmpty}, HostEmpty={HostEmpty}",
                telemetrySettings.Enabled,
                !string.IsNullOrWhiteSpace(telemetrySettings.PostHogApiKey) &&
                telemetrySettings.PostHogApiKey.StartsWith("##", StringComparison.Ordinal),
                string.IsNullOrWhiteSpace(telemetrySettings.PostHogApiKey),
                string.IsNullOrWhiteSpace(telemetrySettings.PostHogHost));

            if (!telemetrySettings.Enabled)
            {
                Log.Information("Telemetry is disabled by user preference in Settings");
            }
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
            .WriteTo.Sink(new TelemetrySerilogSink(Telemetry),
                restrictedToMinimumLevel: telemetryLogLevel)
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
            
            _singleInstanceManager?.Dispose();
        }
    }

    internal static SingleInstanceManager? GetSingleInstanceManager() => _singleInstanceManager;

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI()
            .AfterSetup(_ =>
            {
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    Log.Error(e.Exception, "Unhandled exception in UI thread");
                    // Don't mark as handled - let Avalonia decide whether to crash or not
                };
            });
    }

    private static void RegisterGlobalExceptionHandlers()
    {
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

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
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

    /// <summary>
    ///     Loads the telemetry enabled setting from clientsettings.json.
    ///     Returns true (default) if file doesn't exist or can't be read.
    /// </summary>
    private static bool LoadTelemetryEnabledSetting()
    {
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "clientsettings.json");
            if (!File.Exists(settingsPath))
            {
                return true;
            }

            var json = File.ReadAllText(settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            using var doc = JsonDocument.Parse(json);
            return !doc.RootElement.TryGetProperty("TelemetryEnabled", out var prop) || prop.GetBoolean();
        }
        catch
        {
            return true;
        }
    }
}