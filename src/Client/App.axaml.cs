using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Axorith.Client.CoreSdk;
using Axorith.Client.Services;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axorith.Client;

/// <summary>
///     The main entry point for the Axorith client application.
///     This class is responsible for initializing the application, setting up dependency injection,
///     and creating the main window with gRPC communication to Axorith.Host.
/// </summary>
public class App : Application
{
    /// <summary>
    ///     Gets or sets the application's dependency injection service provider.
    /// </summary>
    public IServiceProvider Services { get; set; } = null!;

    private MainWindow? _mainWindow;
    private bool _isTrayMode;

    /// <summary>
    ///     Loads the application's XAML resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Handles the application's startup logic after the Avalonia framework is ready.
    ///     This method is responsible for:
    ///     1. Setting up logging
    ///     2. Auto-starting Axorith.Host if not running
    ///     3. Establishing gRPC connection to Host
    ///     4. Setting up dependency injection container with CoreSdk API interfaces
    ///     5. Creating and displaying the main window
    ///     6. Registering graceful shutdown handlers
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // Check if running in tray mode (--tray hides window on startup)
        _isTrayMode = Environment.GetCommandLineArgs().Contains("--tray");

        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        var clientConfig = configuration.Get<Configuration>() ?? new Configuration();

        // Setup logging
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
            builder.AddDebug();
        });

        var uiSettingsLogger = loggerFactory.CreateLogger<UISettingsStore>();
        var uiSettingsStore = new UISettingsStore(uiSettingsLogger);
        clientConfig.Ui = uiSettingsStore.LoadOrDefault();

        var logger = loggerFactory.CreateLogger<App>();
        logger.LogInformation("=== Axorith Client starting (Window hidden on startup: {TrayMode}) ===", _isTrayMode);
        logger.LogInformation("Host connection: {Endpoint} (Remote: {IsRemote})",
            clientConfig.Host.GetEndpointUrl(), clientConfig.Host.UseRemoteHost);

        // Phase 1: Build initial service collection
        logger.LogInformation("Building initial service collection...");
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddLogging(); // Add ILogger<T> support
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<LoadingViewModel>();
        services.AddTransient<ErrorViewModel>();
        services.AddSingleton<IWindowStateManager, WindowStateManager>();
        services.AddSingleton(Options.Create(clientConfig));
        services.AddSingleton<IHostController, HostController>();
        services.AddSingleton<IHostTrayService, HostTrayService>();
        services.AddSingleton<IConnectionInitializer, ConnectionInitializer>();
        services.AddSingleton<IClientUiSettingsStore>(_ => uiSettingsStore);

        Services = services.BuildServiceProvider();
        var hostController = Services.GetService<IHostController>();

        // Create window IMMEDIATELY to show UI with loading state
        logger.LogInformation("Initializing Axorith Client UI...");

        var shellViewModel = Services.GetRequiredService<ShellViewModel>();
        var loadingViewModel = Services.GetRequiredService<LoadingViewModel>();
        shellViewModel.Content = loadingViewModel;

        var windowStateManager = Services.GetRequiredService<IWindowStateManager>();

        _mainWindow = new MainWindow
        {
            DataContext = shellViewModel,
            // Always show window in taskbar
            ShowInTaskbar = true
        };

        // If --tray flag, start with window minimized
        if (_isTrayMode)
        {
            logger.LogInformation("Starting with window hidden (--tray flag)");
            _mainWindow.WindowState = WindowState.Minimized;
        }
        else
        {
            logger.LogInformation("Starting with window visible");
            // Restore window state from previous session
            windowStateManager.RestoreWindowState(_mainWindow);
        }

        desktop.MainWindow = _mainWindow;

        var trayService = Services.GetRequiredService<IHostTrayService>();
        trayService.Initialize(desktop, logger);

        // Handle window closing - minimize to tray instead of closing (configurable)
        _mainWindow.Closing += (_, e) =>
        {
            try
            {
                if (_isShuttingDown)
                    return;

                // Prefer current config from DI (may be mutated at runtime via SettingsView)
                var options = Services.GetService<IOptions<Configuration>>();
                var cfg = options?.Value ?? clientConfig;

                if (cfg.Ui.MinimizeToTrayOnClose)
                {
                    e.Cancel = true;
                    _mainWindow.WindowState = WindowState.Minimized;
                    logger.LogInformation("Window minimized to tray");
                }
                else
                {
                    logger.LogInformation("Window closed - shutting down application (MinimizeToTrayOnClose = false)");
                    desktop.Shutdown();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling window closing");
            }
        };

        logger.LogInformation("Window and tray icon created successfully");

        logger.LogInformation("Starting Host connection in background...");
        var connInit = Services.GetRequiredService<IConnectionInitializer>();
        _ = Task.Run(() => connInit.InitializeAsync(this, clientConfig, loggerFactory, logger));

        RegisterShutdownHandler(desktop, loggerFactory, logger);

        base.OnFrameworkInitializationCompleted();
    }


    private bool _isShuttingDown;

    private void RegisterShutdownHandler(IClassicDesktopStyleApplicationLifetime desktop,
        ILoggerFactory loggerFactory, ILogger<App> logger)
    {
        desktop.ShutdownRequested += (_, _) =>
        {
            if (_isShuttingDown)
                return;

            _isShuttingDown = true;

            logger.LogInformation("Client shutting down...");

            var windowStateManager = Services.GetService<IWindowStateManager>();
            if (windowStateManager != null && desktop.MainWindow != null)
                windowStateManager.SaveWindowState(desktop.MainWindow);

            var conn = Services.GetService<ICoreConnection>();
            if (conn != null)
                try
                {
                    conn.DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // Best effort disconnect
                }

            logger.LogInformation("Client shutdown complete");
            loggerFactory.Dispose();
        };
    }
}