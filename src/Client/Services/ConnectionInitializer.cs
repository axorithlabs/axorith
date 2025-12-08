using System.Text.Json;
using Avalonia.Threading;
using Axorith.Client.CoreSdk;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services.Abstractions;
using Axorith.Client.ViewModels;
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
        Environment.ExpandEnvironmentVariables("%AppData%/Axorith"), "host-info.json");

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
                // Dispatch to UI thread is NOT needed here because ToastNotificationService uses Rx Subjects
                // and DesktopNotificationManager observes on UI thread.
                // However, ShellViewModel also observes on UI thread.
                // The service itself is thread-safe.
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
                return;
            }

            await statusUpdater("Starting Axorith Client...", "Checking Axorith.Host status...");

            var isReachable = await controller.IsHostReachableAsync();
            if (!isReachable)
            {
                logger.LogInformation("Host not reachable. Attempting auto-start...");
                await statusUpdater("Starting Axorith Client...", "Starting local Host process...");

                await controller.StartHostAsync(forceRestart: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto-start Host attempt failed. Will try to connect anyway.");
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
        var connection = new GrpcCoreConnection(serverAddress, tokenProvider, connectionLogger);

        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await statusUpdater("Connecting to Axorith.Host...",
                    attempt == 1 ? "Opening secure channel..." : $"Retry {attempt} of {MaxRetries}...");

                await connection.ConnectAsync();
                return connection;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                lastException = ex;
                logger.LogWarning(ex, "Connection attempt {Attempt}/{Max} failed. Retrying in {Delay}ms...",
                    attempt, MaxRetries, RetryDelayMs);
                await Task.Delay(RetryDelayMs);
            }
        }

        if (lastException != null)
        {
            throw new InvalidOperationException(
                $"Failed to connect to Host after {MaxRetries} attempts. Ensure the Host process is running.",
                lastException);
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

        var newProvider = services.BuildServiceProvider();
        app.Services = newProvider;
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

            errorViewModel.Configure(
                $"Initialization error: {errorMessage}\n\nCheck logs for details. Ensure Axorith.Host is running.",
                async () => await InitializeAsync(app, config, loggerFactory, logger));

            shellViewModel.Content = errorViewModel;
        });
    }

    private string GetDiscoveredEndpointUrl(Configuration config, ILogger logger)
    {
        // Try to read port from host-info.json for dynamic port discovery
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

        // Fall back to configured endpoint
        return config.Host.GetEndpointUrl();
    }
}