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
    private static readonly string PendingInstallationPath = Path.Combine(ApplicationPaths.Config, "pending-install.json");
    private static PendingInstallation? _pendingInstallation;
    private static SingleInstanceManager? _singleInstanceManager;
    private static int _applicationReadyHandled;

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

        var telemetryEnabled = TelemetryPreference.ReadOrDefault(LoadLegacyTelemetryEnabledSetting());
        TelemetryPreference.Save(telemetryEnabled);

        var telemetrySettings = new TelemetrySettings()
                .WithEnvironmentOverrides() with
            {
                ApplicationName = "Axorith.Client",
                Enabled = telemetryEnabled
            };

        Telemetry = new TelemetryService(telemetrySettings);
        _pendingInstallation = LoadPendingInstallation();
        if ((_pendingInstallation is null && File.Exists(PendingInstallationPath)) ||
            (_pendingInstallation is { } pending &&
             !IsPendingInstallationEligible(Telemetry.IsEnabled, pending.InstallationId,
                 DeviceIdProvider.GetDeviceId())))
        {
            DiscardPendingInstallation();
        }

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

    internal static void ConfirmPendingInstallation()
    {
        if (_pendingInstallation is not { } pending)
        {
            return;
        }

        if (!IsPendingInstallationEligible(Telemetry is { IsEnabled: true }, pending.InstallationId,
                DeviceIdProvider.GetDeviceId()))
        {
            DiscardPendingInstallation();
            return;
        }

        try
        {
            Telemetry?.TrackEvent("InstallationConfirmed", new Dictionary<string, object?>
            {
                ["installAttemptId"] = pending.InstallAttemptId,
                ["installMode"] = pending.InstallMode,
                ["currentVersion"] = pending.CurrentVersion,
                ["previousVersion"] = pending.PreviousVersion
            });
            File.Delete(PendingInstallationPath);
            _pendingInstallation = null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not clear pending installation telemetry state");
        }
    }

    private static void DiscardPendingInstallation()
    {
        _pendingInstallation = null;
        try
        {
            File.Delete(PendingInstallationPath);
        }
        catch
        {
            // A stale pending attempt must never be retried after telemetry is disabled.
        }
    }

    internal static bool IsPendingInstallationEligible(bool telemetryEnabled, Guid pendingInstallationId,
        string? currentInstallationId) =>
        telemetryEnabled && Guid.TryParse(currentInstallationId, out var parsedInstallationId) &&
        pendingInstallationId == parsedInstallationId;

    internal static void MarkApplicationReady()
    {
        if (Interlocked.Exchange(ref _applicationReadyHandled, 1) != 0)
        {
            return;
        }

        ConfirmPendingInstallation();
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

    private static PendingInstallation? LoadPendingInstallation()
    {
        try
        {
            if (!File.Exists(PendingInstallationPath)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(PendingInstallationPath));
            var root = document.RootElement;
            if (!Guid.TryParse(root.GetProperty("installationId").GetString(), out var installationId))
                return null;
            if (!Guid.TryParse(root.GetProperty("installAttemptId").GetString(), out var installAttemptId))
                return null;
            return new PendingInstallation(
                installationId,
                installAttemptId,
                root.GetProperty("installMode").GetString() ?? string.Empty,
                root.GetProperty("currentVersion").GetString() ?? string.Empty,
                root.TryGetProperty("previousVersion", out var previousVersion) && previousVersion.ValueKind == JsonValueKind.String
                    ? previousVersion.GetString()
                    : null);
        }
        catch
        {
            return null;
        }
    }

    private sealed record PendingInstallation(Guid InstallationId, Guid InstallAttemptId, string InstallMode,
        string CurrentVersion, string? PreviousVersion);
}
