using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Axorith.Client.Services;
using Axorith.Shared.Utils;
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
    internal static IConfigurationRoot? RuntimeConfiguration { get; private set; }
    private static SingleInstanceManager? _singleInstanceManager;

    [STAThread]
    public static int Main(string[] args)
    {
        var earlyLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        var earlyLoggerFactory = new SerilogLoggerFactory(earlyLogger);
        var singleInstanceLogger = earlyLoggerFactory.CreateLogger<SingleInstanceManager>();

        _singleInstanceManager = new SingleInstanceManager(singleInstanceLogger);

        if (!_singleInstanceManager.TryAcquireLock())
        {
            earlyLogger.Information("Another instance detected - sending activation request");

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
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.development.json"), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        RuntimeConfiguration = configuration;

        var isTestRun = Environment.GetEnvironmentVariable("AXORITH_TEST_MODE") == "1";
        var telemetryEnabled = !isTestRun && TelemetryPreference.ReadOrDefault(LoadLegacyTelemetryEnabledSetting());
        if (!isTestRun)
        {
            TelemetryPreference.Save(telemetryEnabled);
        }

        var telemetrySettings = new TelemetrySettings()
                .WithEnvironmentOverrides() with
            {
                ApplicationName = "Axorith.Client",
                Enabled = telemetryEnabled
            };

        Telemetry = isTestRun ? NoopTelemetryService.Instance : new TelemetryService(telemetrySettings);

        TelemetryRuntime.LogConfiguration("Client", telemetrySettings, Telemetry.IsEnabled);
        if (!telemetrySettings.IsActive && !telemetrySettings.Enabled)
            Log.Information("Telemetry is disabled by user preference in Settings");

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
            .CreateLogger();

        try
        {
            Log.Information("Axorith Client starting");
            Log.Information("Version: {Version}, OS: {OS}",
                typeof(Program).Assembly.GetName().Version,
                Environment.OSVersion);

            var app = BuildAvaloniaApp();

            TelemetryRuntime.RegisterGlobalExceptionHandlers(Telemetry, "client");
            Log.Debug("Global exception handlers registered");

            app.StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

            Log.Information("Axorith Client shut down gracefully");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Axorith Client terminated unexpectedly");
            Telemetry?.TrackError(ex, "client", "startup", "fatal", handled: true, fatal: true);

            return 1;
        }
        finally
        {
            Log.CloseAndFlush();

            using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Telemetry?.FlushAsync(flushCts.Token).GetAwaiter().GetResult();

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
                    Telemetry?.TrackError(e.Exception, "client", "startup", "error", handled: e.Handled,
                        fatal: false);
                    // Don't mark as handled - let Avalonia decide whether to crash or not
                };
            });
    }

    private static bool LoadLegacyTelemetryEnabledSetting()
    {
        try
        {
            var settingsPath = Path.Combine(ApplicationPaths.Config, "clientsettings.json");
            if (!File.Exists(settingsPath))
            {
                settingsPath = Path.Combine(AppContext.BaseDirectory, "clientsettings.json");
            }
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
