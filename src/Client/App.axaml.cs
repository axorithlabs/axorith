using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services;
using Axorith.Client.Services.Abstractions;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Axorith.Shared.Platform;
using Axorith.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using WindowState = Avalonia.Controls.WindowState;

namespace Axorith.Client;

public class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    private MainWindow? _mainWindow;
    private bool _isTrayMode;
    private DesktopNotificationManager? _notificationManager;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _isTrayMode = Environment.GetCommandLineArgs().Contains("--tray");

        var configuration = Program.RuntimeConfiguration
            ?? throw new InvalidOperationException("Client configuration has not been loaded.");
        var clientConfig = configuration.Get<Configuration>() ?? new Configuration();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        var telemetry = Program.Telemetry ?? NoopTelemetryService.Instance;

        var uiSettingsLogger = loggerFactory.CreateLogger<UiSettingsStore>();
        var uiSettingsStore = new UiSettingsStore(uiSettingsLogger);
        clientConfig.Ui = uiSettingsStore.LoadOrDefault();

        var logger = loggerFactory.CreateLogger<App>();
        logger.LogInformation("Axorith Client starting (Window hidden on startup: {TrayMode})", _isTrayMode);
        logger.LogInformation("Host connection: {Endpoint} (Remote: {IsRemote})",
            clientConfig.Host.GetEndpointUrl(), clientConfig.Host.UseRemoteHost);

        logger.LogInformation("Building initial service collection...");
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddLogging();
        services.AddSingleton(telemetry);

        services.AddSingleton<IToastNotificationService, ToastNotificationService>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<LoadingViewModel>();
        services.AddTransient<ErrorViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<SessionEditorViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<WindowStateManager>();
        services.AddSingleton(Options.Create(clientConfig));
        services.AddSingleton<HostController>();
        services.AddSingleton<ITokenProvider, FileTokenProvider>();
        services.AddSingleton<CoreConnectionHolder>();
        services.AddTransient<IPresetsApi>(sp => sp.GetRequiredService<CoreConnectionHolder>().GetRequiredConnection().Presets);
        services.AddTransient<ISessionsApi>(sp => sp.GetRequiredService<CoreConnectionHolder>().GetRequiredConnection().Sessions);
        services.AddTransient<IModulesApi>(sp => sp.GetRequiredService<CoreConnectionHolder>().GetRequiredConnection().Modules);
        services.AddTransient<ISchedulerApi>(sp => sp.GetRequiredService<CoreConnectionHolder>().GetRequiredConnection().Scheduler);
        services.AddTransient<INotificationApi>(sp => sp.GetRequiredService<CoreConnectionHolder>().GetRequiredConnection().Notifications);
        services.AddTransient<IUpdatesApi>(sp => sp.GetRequiredService<CoreConnectionHolder>().GetRequiredConnection().Updates);
        services.AddSingleton<HostHealthMonitor>();
        services.AddSingleton<HostTrayService>();
        services.AddSingleton<ConnectionInitializer>();
        services.AddSingleton<IClientUiSettingsStore>(_ => uiSettingsStore);
        services.AddSingleton<FilePickerService>(_ => new FilePickerService(desktop));
        services.AddSingleton(sp => new DesktopNotificationManager(
            sp.GetRequiredService<IToastNotificationService>(), desktop));
        services.AddSingleton<IAutoStartManager>(sp => PlatformServices.CreateAutoStartManager(
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<IAutoStartManager>()));
        services.AddSingleton<IAppDiscoveryService>(sp =>
            PlatformServices.CreateAppDiscoveryService(sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<ClientOnboardingService>();

        Services = services.BuildServiceProvider();

        var shellViewModel = Services.GetRequiredService<ShellViewModel>();
        shellViewModel.Services = Services;
        var loadingViewModel = Services.GetRequiredService<LoadingViewModel>();
        shellViewModel.Content = loadingViewModel;

        var windowStateManager = Services.GetRequiredService<WindowStateManager>();

        _mainWindow = new MainWindow
        {
            DataContext = shellViewModel
        };

        desktop.MainWindow = _mainWindow;

        windowStateManager.RestoreWindowState(_mainWindow);

        if (_isTrayMode)
        {
            logger.LogInformation("Starting with window hidden (--tray flag)");
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.WindowState = WindowState.Minimized;
        }
        else
        {
            logger.LogInformation("Starting with window visible");
            _mainWindow.ShowInTaskbar = true;
        }

        var trayService = Services.GetRequiredService<HostTrayService>();
        trayService.Initialize(desktop, logger);

        _notificationManager = Services.GetRequiredService<DesktopNotificationManager>();
        _notificationManager.Initialize();

        var singleInstanceManager = Program.GetSingleInstanceManager();
        if (singleInstanceManager != null)
        {
            singleInstanceManager.ActivationRequested += (_, _) =>
            {
                logger.LogInformation("Activation requested from another instance");
                Dispatcher.UIThread.Post(() => ActivateMainWindow());
            };
        }

        ApplyAutoStartSettings(uiSettingsStore, Services, logger);

        _mainWindow.Closing += (_, e) =>
        {
            try
            {
                if (_isShuttingDown)
                {
                    return;
                }

                if (_mainWindow.WindowState != WindowState.Minimized)
                {
                    windowStateManager.SaveWindowState(_mainWindow);
                }

                var options = Services.GetService<IOptions<Configuration>>();
                var cfg = options?.Value ?? clientConfig;

                if (cfg.Ui.MinimizeToTrayOnClose)
                {
                    e.Cancel = true;
                    _mainWindow.WindowState = WindowState.Minimized;
                    _mainWindow.ShowInTaskbar = false;
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

        RegisterShutdownHandler(desktop, logger);

        base.OnFrameworkInitializationCompleted();

        logger.LogInformation("Window shown. Starting Host connection in background...");
        var connInit = Services.GetRequiredService<ConnectionInitializer>();
        _ = Task.Run(() => connInit.InitializeAsync(this, clientConfig, loggerFactory, logger));
    }

    private bool _isShuttingDown;

    private void RegisterShutdownHandler(IClassicDesktopStyleApplicationLifetime desktop, ILogger<App> logger)
    {
        desktop.ShutdownRequested += (_, _) =>
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;

            logger.LogInformation("Client shutting down...");

            _notificationManager?.Dispose();

            var windowStateManager = Services.GetService<WindowStateManager>();
            if (windowStateManager != null && desktop.MainWindow is { WindowState: not WindowState.Minimized } mainWindow)
            {
                windowStateManager.SaveWindowState(mainWindow);
            }

            try
            {
                Services.GetService<CoreConnectionHolder>()?.CloseAsync().Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disconnecting client");
            }

            logger.LogInformation("Client shutdown complete");
        };
    }

    private static void ApplyAutoStartSettings(UiSettingsStore settingsStore, IServiceProvider services,
        ILogger<App> logger)
    {
        try
        {
            var config = settingsStore.LoadOrDefault();
            var autoStartManager = services.GetService<IAutoStartManager>();

            if (autoStartManager == null)
            {
                return;
            }

            if (config.AutoStartEnabled && !autoStartManager.IsAutoStartEnabled)
            {
                autoStartManager.EnableAutoStart(config.AutoStartMinimized);
                logger.LogInformation("Auto-start enabled on first run");
            }
            else if (!config.AutoStartEnabled && autoStartManager.IsAutoStartEnabled)
            {
                autoStartManager.DisableAutoStart();
                logger.LogInformation("Auto-start disabled per user settings");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply auto-start settings");
        }
    }

    private void ActivateMainWindow()
    {
        if (_mainWindow == null)
        {
            return;
        }

        try
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            if (!_mainWindow.ShowInTaskbar)
            {
                _mainWindow.ShowInTaskbar = true;
            }

            _mainWindow.Show();
            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
            _mainWindow.Focus();

            var logger = Services.GetService<ILogger<App>>();
            logger?.LogInformation("Main window activated");
        }
        catch (Exception ex)
        {
            var logger = Services.GetService<ILogger<App>>();
            logger?.LogError(ex, "Failed to activate main window");
        }
    }
}
