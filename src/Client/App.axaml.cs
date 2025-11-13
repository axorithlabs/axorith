using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Axorith.Client.CoreSdk;
using Axorith.Client.CoreSdk.Grpc;
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
    ///     Gets the application's dependency injection service provider.
    /// </summary>
    public IServiceProvider Services { get; private set; } = null!;

    private TrayIcon? _trayIcon;
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

        var clientConfig = configuration.Get<ClientConfiguration>() ?? new ClientConfiguration();

        // Setup logging
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
            builder.AddDebug();
        });

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

        Services = services.BuildServiceProvider();

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

        // Always create tray icon
        CreateTrayIcon(desktop, logger);

        // Handle window closing - minimize to tray instead of closing
        _mainWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            _mainWindow.WindowState = WindowState.Minimized;
            logger.LogInformation("Window minimized to tray");
        };

        logger.LogInformation("Window and tray icon created successfully");

        logger.LogInformation("Starting Host connection in background...");

        // Start Host connection asynchronously WITHOUT blocking UI
        _ = Task.Run(async () =>
        {
            try
            {
                await InitializeConnectionAsync(shellViewModel, loadingViewModel, loggerFactory, logger, clientConfig)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Background initialization failed");
            }
        });

        // Register shutdown handler
        RegisterShutdownHandler(desktop, loggerFactory, logger);

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop, ILogger<App> logger)
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "Axorith Client",
            Icon = new WindowIcon("Assets/icon.png"),
            IsVisible = true
        };

        var showItem = new NativeMenuItem("Show");
        showItem.Click += (_, _) => { ShowWindow(logger); };

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            logger.LogInformation("Exit requested from tray");
            if (desktop is IClassicDesktopStyleApplicationLifetime desktopLifetime)
                desktopLifetime.Shutdown();
        };

        var menu = new NativeMenu
        {
            new NativeMenuItem("Axorith Client") { IsEnabled = false },
            new NativeMenuItemSeparator(),
            showItem,
            new NativeMenuItemSeparator(),
            exitItem
        };

        _trayIcon.Menu = menu;

        // Handle double-click on tray icon
        _trayIcon.Clicked += (_, _) => { ShowWindow(logger); };

        var trayIcons = new TrayIcons { _trayIcon };
        SetValue(TrayIcon.IconsProperty, trayIcons);

        logger.LogInformation("Tray icon created");
    }

    private void ShowWindow(ILogger<App> logger)
    {
        if (_mainWindow == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
                _mainWindow.BringIntoView();
                logger.LogInformation("Window shown from tray");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to show window from tray");
            }
        });
    }

    private async Task InitializeConnectionAsync(ShellViewModel shellViewModel, LoadingViewModel loadingViewModel,
        ILoggerFactory loggerFactory, ILogger<App> logger, ClientConfiguration clientConfig)
    {
        try
        {
            var serverAddress = clientConfig.Host.GetEndpointUrl();

            logger.LogInformation("Connecting to Host at {Address}...", serverAddress);

            var connection = new GrpcCoreConnection(
                serverAddress,
                loggerFactory.CreateLogger<GrpcCoreConnection>());

            // Retry connection with exponential backoff
            const int maxRetries = 5;
            Exception? lastException = null;

            for (var attempt = 1; attempt <= maxRetries; attempt++)
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        loadingViewModel.Message = attempt == 1
                            ? "Connecting to Axorith.Host..."
                            : $"Connecting to Axorith.Host (attempt {attempt}/{maxRetries})...";
                    });

                    await connection.ConnectAsync();
                    logger.LogInformation("Connected successfully to Host on attempt {Attempt}", attempt);
                    break; // Success - exit retry loop
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    var delayMs = (int)Math.Pow(2, attempt - 1) * 1000; // 1s, 2s, 4s, 8s, 16s
                    logger.LogWarning(ex, "Connection attempt {Attempt}/{MaxRetries} failed, retrying in {DelayMs}ms",
                        attempt, maxRetries, delayMs);
                    await Task.Delay(delayMs);
                }

            // If all retries failed, throw the last exception
            if (lastException != null)
            {
                logger.LogError(lastException, "All {MaxRetries} connection attempts failed", maxRetries);
                throw new InvalidOperationException($"Failed to connect to Host after {maxRetries} attempts",
                    lastException);
            }

            // Register real services
            var newServices = new ServiceCollection();
            newServices.AddSingleton(Options.Create(clientConfig));
            newServices.AddSingleton(loggerFactory);
            newServices.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            newServices.AddSingleton<ICoreConnection>(connection);
            newServices.AddSingleton(connection.Presets);
            newServices.AddSingleton(connection.Sessions);
            newServices.AddSingleton(connection.Modules);
            newServices.AddSingleton(connection.Diagnostics);
            newServices.AddSingleton<IHostHealthMonitor, HostHealthMonitor>();
            newServices.AddSingleton(shellViewModel);
            newServices.AddTransient<MainViewModel>();
            newServices.AddTransient<SessionEditorViewModel>();

            // Dispose old provider to prevent memory leak
            var oldProvider = Services as IDisposable;
            Services = newServices.BuildServiceProvider();
            oldProvider?.Dispose();
            logger.LogInformation("ServiceProvider recreated (old provider disposed)");

            await Dispatcher.UIThread.InvokeAsync(() => { loadingViewModel.Message = "Loading presets..."; });

            logger.LogInformation("Creating MainViewModel on UI thread...");
            var mainViewModel = await Dispatcher.UIThread.InvokeAsync(() =>
                Services.GetRequiredService<MainViewModel>());

            logger.LogInformation("Loading preset data...");
            await mainViewModel.InitializeAsync();

            await Dispatcher.UIThread.InvokeAsync(() => { shellViewModel.Content = mainViewModel; });

            // Start health monitoring
            logger.LogInformation("Starting host health monitoring...");
            var healthMonitor = Services.GetRequiredService<IHostHealthMonitor>();
            healthMonitor.HostUnhealthy += () =>
            {
                logger.LogWarning("Host became unhealthy - showing error page");

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var errorViewModel = new ErrorViewModel();
                        errorViewModel.Configure(
                            "Lost connection to Axorith.Host.\n\n" +
                            "Please start Axorith using the Service Manager tray icon.",
                            null,
                            async () =>
                            {
                                var loadingVm = new LoadingViewModel();
                                shellViewModel.Content = loadingVm;
                                await InitializeConnectionAsync(shellViewModel, loadingVm, loggerFactory, logger,
                                    clientConfig).ConfigureAwait(false);
                            });
                        shellViewModel.Content = errorViewModel;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to show error page for unhealthy host");
                    }
                });
            };
            healthMonitor.Start();

            logger.LogInformation("Axorith Client initialization complete");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to Host");
            logger.LogError(ex, "Failed to initialize Host connection");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var errorViewModel = Services.GetRequiredService<ErrorViewModel>();
                errorViewModel.Configure($"Initialization error: {ex.Message}");
                shellViewModel.Content = errorViewModel;
            });
        }
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

            // Save window state
            var windowStateManager = Services.GetService<IWindowStateManager>();
            if (windowStateManager != null && desktop.MainWindow != null)
                windowStateManager.SaveWindowState(desktop.MainWindow);

            // Disconnect from Host
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