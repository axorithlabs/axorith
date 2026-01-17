using System.Text.Json;
using Avalonia.Threading;
using Axorith.Client.CoreSdk;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services.Abstractions;
using Axorith.Client.ViewModels;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axorith.Client.Services;

public sealed class ConnectionInitializer : IConnectionInitializer
{
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 1000;

    private static readonly string HostInfoPath = Path.Combine(
        Environment.ExpandEnvironmentVariables("%AppData%/Axorith"), "config", "host-info.json");

    public async Task InitializeAsync(App app, Configuration config, ILoggerFactory loggerFactory, ILogger<App> logger)
    {
        var shellViewModel = app.Services.GetRequiredService<ShellViewModel>();
        var loadingViewModel = app.Services.GetRequiredService<LoadingViewModel>();

        async Task UpdateStatus(string message, string? subMessage = null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (shellViewModel.Content != loadingViewModel)
                {
                    shellViewModel.Content = loadingViewModel;
                }

                loadingViewModel.Message = message;
                loadingViewModel.SubMessage = subMessage;
            });
        }

        try
        {
            await UpdateStatus("Starting Axorith Client...", "Initializing environment...");

            if (config.Host is { UseRemoteHost: false, AutoStartHost: true })
            {
                await EnsureHostRunningAsync(app.Services, logger, UpdateStatus);
            }

            var serverAddress = GetDiscoveredEndpointUrl(config, logger);
            logger.LogInformation("Connecting to Host at {Address}...", serverAddress);

            var tokenProvider = app.Services.GetRequiredService<ITokenProvider>();
            var connection = await ConnectWithRetryAsync(
                serverAddress,
                tokenProvider,
                loggerFactory,
                logger,
                UpdateStatus);

            await UpdateStatus("Connected to Axorith.Host", "Initializing client services...");
            RebuildServiceProvider(app, config, loggerFactory, connection, logger);

            var modulesApi = app.Services.GetRequiredService<IModulesApi>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await modulesApi.ListModulesAsync().ConfigureAwait(false);
                }
                catch (Exception warmEx)
                {
                    logger.LogWarning(warmEx, "Modules cache warm-up failed");
                }
            });

            var notificationService = app.Services.GetRequiredService<INotificationApi>();
            var toastService = app.Services.GetRequiredService<IToastNotificationService>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await SubscribeToNotifications(notificationService, toastService, logger).ConfigureAwait(false);
                }
                catch (Exception subEx)
                {
                    logger.LogWarning(subEx, "Notification subscription task failed");
                }
            });

            await UpdateStatus("Loading presets...", "Fetching session data...");

            var mainViewModel = app.Services.GetRequiredService<MainViewModel>();

            await mainViewModel.InitializeAsync();

            await UpdateStatus("Ready", "Axorith Client is ready.");

            await Dispatcher.UIThread.InvokeAsync(() => { shellViewModel.Content = mainViewModel; });

            StartHealthMonitoring(app.Services, app, config, loggerFactory, logger);

            logger.LogInformation("Axorith Client initialization sequence complete.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal initialization error");
            await ShowFatalErrorAsync(app, config, loggerFactory, logger, ex.Message);
        }
    }

    private async Task SubscribeToNotifications(INotificationApi api, IToastNotificationService toastService,
        ILogger logger)
    {
        try
        {
            await foreach (var notification in api.StreamNotificationsAsync())
            {
                toastService.Show(notification.Message, notification.Type);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notification stream disconnected");
        }
    }

    private async Task EnsureHostRunningAsync(
        IServiceProvider services,
        ILogger logger,
        Func<string, string?, Task> statusUpdater)
    {
        try
        {
            var controller = services.GetService<IHostController>();
            if (controller == null)
            {
                logger.LogWarning("HostController not available, skipping auto-start");
                return;
            }

            await statusUpdater("Starting Axorith Client...", "Checking Axorith.Host status...");

            var isReachable = await controller.IsHostReachableAsync();
            if (!isReachable)
            {
                logger.LogInformation("Host not reachable. Attempting auto-start...");
                await statusUpdater("Starting Axorith Client...", "Starting local Host process...");

                // CRITICAL FIX: Use forceRestart: false to allow existing Host to initialize
                // This prevents killing a Host that's still starting up
                await controller.StartHostAsync(forceRestart: false);
                
                // Give Host additional time to become fully ready after file write
                await statusUpdater("Starting Axorith Client...", "Waiting for Host to initialize (this may take 10-15 seconds)...");
                
                // Wait and verify Host actually started
                var verifyStarted = false;
                for (var i = 0; i < 10; i++)
                {
                    await Task.Delay(1000);
                    if (await controller.IsHostReachableAsync())
                    {
                        verifyStarted = true;
                        logger.LogInformation("Host verified as reachable after {Seconds}s", i + 1);
                        break;
                    }
                }
                
                if (!verifyStarted)
                {
                    logger.LogWarning("Host auto-start completed but Host is not reachable. Will attempt connection anyway.");
                }
            }
            else
            {
                logger.LogInformation("Host is already reachable. Skipping auto-start.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-start Host attempt failed: {Message}. Will try to connect anyway.", ex.Message);
            // Don't throw - let connection attempt handle the error with better UI feedback
        }
    }

    private async Task<GrpcCoreConnection> ConnectWithRetryAsync(
        string serverAddress,
        ITokenProvider tokenProvider,
        ILoggerFactory loggerFactory,
        ILogger logger,
        Func<string, string?, Task> statusUpdater)
    {
        var connectionLogger = loggerFactory.CreateLogger<GrpcCoreConnection>();
        var connection = new GrpcCoreConnection(serverAddress, tokenProvider, connectionLogger, loggerFactory);

        Exception? lastException = null;
        var maxRetries = 5; // Increased from 3 to 5
        var retryDelayMs = 2000; // Increased from 1s to 2s

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var statusMessage = attempt == 1 
                    ? "Opening secure channel..." 
                    : $"Retry {attempt} of {maxRetries} (waiting {retryDelayMs / 1000}s between attempts)...";
                    
                await statusUpdater("Connecting to Axorith.Host...", statusMessage);

                await connection.ConnectAsync();
                
                logger.LogInformation("Successfully connected to Host on attempt {Attempt}", attempt);
                return connection;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                lastException = ex;
                logger.LogWarning(ex, "Connection attempt {Attempt}/{Max} failed: {Message}. Retrying in {Delay}ms...",
                    attempt, maxRetries, ex.Message, retryDelayMs);
                await Task.Delay(retryDelayMs, default);
            }
            catch (Exception ex)
            {
                // Last attempt failed
                lastException = ex;
                logger.LogError(ex, "Final connection attempt {Attempt}/{Max} failed: {Message}",
                    attempt, maxRetries, ex.Message);
            }
        }

        if (lastException != null)
        {
            var errorMessage = $"Failed to connect to Host after {maxRetries} attempts.\n\n" +
                              $"Last error: {lastException.Message}\n\n" +
                              $"Possible causes:\n" +
                              $"• Host process failed to start\n" +
                              $"• Port {serverAddress} is blocked by firewall\n" +
                              $"• Another instance is using the port\n" +
                              $"• Host crashed during initialization\n\n" +
                              $"Check logs at: {ApplicationPaths.Logs}";
            
            throw new InvalidOperationException(errorMessage, lastException);
        }

        throw new InvalidOperationException("Connection failed with unknown error.");
    }

    private void RebuildServiceProvider(
        App app,
        Configuration config,
        ILoggerFactory loggerFactory,
        ICoreConnection connection,
        ILogger logger)
    {
        logger.LogInformation("Rebuilding ServiceProvider with active connection...");

        var services = new ServiceCollection();

        services.AddSingleton(Options.Create(config));
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(app.Services.GetRequiredService<ITelemetryService>());

        services.AddSingleton(connection);
        services.AddSingleton(connection.Presets);
        services.AddSingleton(connection.Sessions);
        services.AddSingleton(connection.Modules);
        services.AddSingleton(connection.Diagnostics);
        services.AddSingleton(connection.Scheduler);
        services.AddSingleton(connection.Notifications);
        services.AddSingleton(connection.Updates);

        var existingMonitor = app.Services.GetRequiredService<IHostHealthMonitor>();
        existingMonitor.SetDiagnosticsApi(connection.Diagnostics);
        services.AddSingleton(existingMonitor);

        services.AddSingleton<IHostController, HostController>();
        services.AddSingleton<ITokenProvider>(app.Services.GetRequiredService<ITokenProvider>());
        services.AddSingleton<IClientUiSettingsStore, UiSettingsStore>();

        services.AddSingleton(app.Services.GetRequiredService<IToastNotificationService>());
        services.AddSingleton(app.Services.GetRequiredService<DesktopNotificationManager>());

        var filePicker = app.Services.GetService<IFilePickerService>();
        if (filePicker != null)
        {
            services.AddSingleton(filePicker);
        }

        services.AddSingleton(app.Services.GetRequiredService<ShellViewModel>());
        services.AddTransient<LoadingViewModel>();
        services.AddTransient<ErrorViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<SessionEditorViewModel>();

        var autoStartManager = app.Services.GetService<IAutoStartManager>();
        if (autoStartManager != null)
        {
            services.AddSingleton(autoStartManager);
        }

        services.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<ShellViewModel>(),
            sp.GetRequiredService<IClientUiSettingsStore>(),
            sp.GetService<IAutoStartManager>() ?? new NoOpAutoStartManager(),
            sp.GetRequiredService<ITelemetryService>(),
            sp.GetRequiredService<IOptions<Configuration>>(),
            sp,
            sp.GetRequiredService<ILogger<SettingsViewModel>>()));

        services.AddSingleton<IAppDiscoveryService>(_ => PlatformServices.CreateAppDiscoveryService(loggerFactory));
        services.AddSingleton<IClientOnboardingService, ClientOnboardingService>();

        var newProvider = services.BuildServiceProvider();
        app.Services = newProvider;

        var shell = newProvider.GetRequiredService<ShellViewModel>();
        shell.Services = newProvider;
    }

    private void StartHealthMonitoring(
        IServiceProvider services,
        App app,
        Configuration config,
        ILoggerFactory loggerFactory,
        ILogger<App> logger)
    {
        var healthMonitor = services.GetRequiredService<IHostHealthMonitor>();
        var shellViewModel = services.GetRequiredService<ShellViewModel>();

        healthMonitor.HostUnhealthy += () =>
        {
            logger.LogWarning("Host became unhealthy - triggering error flow.");
            Dispatcher.UIThread.Post(() =>
            {
                var errorViewModel = services.GetRequiredService<ErrorViewModel>();
                errorViewModel.Configure(
                    "Lost connection to Axorith.Host.\n\nRestart the Host using the tray menu, then click 'Retry'.",
                    async () =>
                    {
                        shellViewModel.Content = new LoadingViewModel();
                        await InitializeAsync(app, config, loggerFactory, logger);
                    });
                shellViewModel.Content = errorViewModel;
            });
        };

        healthMonitor.Start();
    }

    private async Task ShowFatalErrorAsync(
        App app,
        Configuration config,
        ILoggerFactory loggerFactory,
        ILogger<App> logger,
        string errorMessage)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var shellViewModel = app.Services.GetRequiredService<ShellViewModel>();
            var errorViewModel = app.Services.GetRequiredService<ErrorViewModel>();

            // Enhanced error message with actionable information
            var enhancedMessage = $"❌ Failed to start Axorith\n\n{errorMessage}\n\n" +
                                 $"📁 Log files: {ApplicationPaths.Logs}\n\n" +
                                 $"💡 Troubleshooting:\n" +
                                 $"1. Check if another Axorith instance is running\n" +
                                 $"2. Restart your computer to free up ports\n" +
                                 $"3. Check antivirus/firewall settings\n" +
                                 $"4. Run as Administrator if needed\n\n" +
                                 $"Click 'Retry' to try again, or check logs for details.";

            errorViewModel.Configure(
                enhancedMessage,
                async () => await InitializeAsync(app, config, loggerFactory, logger));

            shellViewModel.Content = errorViewModel;
        });
    }

    private string GetDiscoveredEndpointUrl(Configuration config, ILogger logger)
    {
        try
        {
            if (File.Exists(HostInfoPath))
            {
                var json = File.ReadAllText(HostInfoPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("port", out var portElement))
                {
                    var port = portElement.GetInt32();
                    var address = config.Host.Address;
                    logger.LogDebug("Discovered host port {Port} from host-info.json", port);
                    return $"http://{address}:{port}";
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read host-info.json, using configured endpoint");
        }

        return config.Host.GetEndpointUrl();
    }
}