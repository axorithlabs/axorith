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

namespace Axorith.Client.Services;

public sealed class ConnectionInitializer
{
    private int _healthMonitoringStarted;
    private static readonly string HostInfoPath = ApplicationPaths.HostInfoFile;

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

            var serverAddress = config.Host.GetEndpointUrl();
            if (!config.Host.UseRemoteHost && HostInfoReader.TryReadPort(HostInfoPath, out var discoveredPort))
            {
                serverAddress = $"http://{config.Host.Address}:{discoveredPort}";
                logger.LogDebug("Discovered host port {Port} from host-info.json", discoveredPort);
            }

            logger.LogInformation("Connecting to Host at {Address}...", serverAddress);

            var tokenProvider = app.Services.GetRequiredService<ITokenProvider>();
            var connection = await ConnectWithRetryAsync(
                serverAddress,
                tokenProvider,
                loggerFactory,
                logger,
                UpdateStatus);

            await UpdateStatus("Connected to Axorith.Host", "Initializing client services...");
            await app.Services.GetRequiredService<CoreConnectionHolder>().ReplaceAsync(connection).ConfigureAwait(false);
            app.Services.GetRequiredService<HostHealthMonitor>().SetDiagnosticsApi(connection.Diagnostics);

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
            Program.MarkApplicationReady();

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
                toastService.Show(notification.Message, notification.Type, notification.Source);
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
            var controller = services.GetService<HostController>();
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

                await controller.StartHostAsync(forceRestart: false);

                await statusUpdater("Starting Axorith Client...",
                    "Waiting for Host to initialize (this may take 10-15 seconds)...");

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
                    logger.LogWarning(
                        "Host auto-start completed but Host is not reachable. Will attempt connection anyway.");
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
        const int maxRetries = 5;
        const int retryDelayMs = 2_000;

        Exception? lastException = null;

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

    private void StartHealthMonitoring(
        IServiceProvider services,
        App app,
        Configuration config,
        ILoggerFactory loggerFactory,
        ILogger<App> logger)
    {
        if (Interlocked.Exchange(ref _healthMonitoringStarted, 1) != 0)
        {
            return;
        }

        var healthMonitor = services.GetRequiredService<HostHealthMonitor>();
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


}
