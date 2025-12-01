using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.Configuration;
using ReactiveUI.Avalonia;
using Serilog;

namespace Axorith.Client;

internal static class Program
{
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
            Log.Information("=== Axorith Client starting ===");
            Log.Information("Version: {Version}, OS: {OS}", 
                typeof(Program).Assembly.GetName().Version, 
                Environment.OSVersion);

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
            }
            else
            {
                Log.Error(exception, "Unhandled exception in AppDomain (non-terminating)");
            }
        };

        // Catch unobserved task exceptions
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved(); // Prevent process termination
        };

        Log.Debug("Global exception handlers registered");
    }
}