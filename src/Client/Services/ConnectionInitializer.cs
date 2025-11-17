using Avalonia.Threading;
using Axorith.Client.CoreSdk;
using Axorith.Client.CoreSdk.Grpc;
using Axorith.Client.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axorith.Client.Services;

public interface IConnectionInitializer
{
    Task InitializeAsync(App app, Configuration config, ILoggerFactory loggerFactory, ILogger<App> logger);
}

public sealed class ConnectionInitializer : IConnectionInitializer
{
    public async Task InitializeAsync(App app, Configuration config, ILoggerFactory loggerFactory,
        ILogger<App> logger)
    {
        var shellViewModel = app.Services.GetRequiredService<ShellViewModel>();
        var loadingViewModel = app.Services.GetRequiredService<LoadingViewModel>();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            shellViewModel.Content = loadingViewModel;
            loadingViewModel.Message = "Starting Axorith Client...";
            loadingViewModel.SubMessage = "Preparing connection to Axorith.Host";
        });

        try
        {
            var serverAddress = config.Host.GetEndpointUrl();
            logger.LogInformation("Connecting to Host at {Address}...", serverAddress);

            if (config.Host is { UseRemoteHost: false, AutoStartHost: true })
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        loadingViewModel.Message = "Starting Axorith Client...";
                        loadingViewModel.SubMessage = "Checking Axorith.Host status...";
                    });

                    var controller = app.Services.GetService<IHostController>();
                    if (controller != null)
                    {
                        var healthy = await controller.IsHostReachableAsync();
                        if (!healthy)
                        {
                            logger.LogInformation("Auto-starting local Host...");

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                loadingViewModel.SubMessage = "Starting Axorith.Host...";
                            });

                            await controller.StartHostAsync();
                        }
                        else
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                loadingViewModel.SubMessage = "Axorith.Host is already running";
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Auto-start Host attempt failed");
                }

            var connection = new GrpcCoreConnection(
                serverAddress,
                loggerFactory.CreateLogger<GrpcCoreConnection>());

            const int maxRetries = 3;
            Exception? lastException = null;

            for (var attempt = 1; attempt <= maxRetries; attempt++)
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        loadingViewModel.Message = "Connecting to Axorith.Host...";
                        loadingViewModel.SubMessage = attempt == 1
                            ? "Opening gRPC channel..."
                            : $"Retry {attempt} of {maxRetries}";
                    });

                    await connection.ConnectAsync();
                    logger.LogInformation("Connected successfully to Host on attempt {Attempt}", attempt);
                    break;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    const int delayMs = 1000;
                    logger.LogWarning(ex,
                        "Connection attempt {Attempt}/{Max} failed, retrying in {DelayMs}ms",
                        attempt, maxRetries, delayMs);
                    await Task.Delay(delayMs);
                }

            if (lastException != null)
            {
                logger.LogError(lastException, "All {MaxRetries} connection attempts failed", maxRetries);
                throw new InvalidOperationException($"Failed to connect to Host after {maxRetries} attempts",
                    lastException);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                loadingViewModel.Message = "Connected to Axorith.Host";
                loadingViewModel.SubMessage = "Initializing client services...";
            });

            var newServices = new ServiceCollection();
            newServices.AddSingleton(Options.Create(config));
            newServices.AddSingleton(loggerFactory);
            newServices.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
            newServices.AddSingleton<ICoreConnection>(connection);
            newServices.AddSingleton(connection.Presets);
            newServices.AddSingleton(connection.Sessions);
            newServices.AddSingleton(connection.Modules);
            newServices.AddSingleton(connection.Diagnostics);
            newServices.AddSingleton<IHostHealthMonitor, HostHealthMonitor>();
            newServices.AddSingleton<IHostController, HostController>();
            newServices.AddSingleton(shellViewModel);
            newServices.AddTransient<LoadingViewModel>();
            newServices.AddTransient<ErrorViewModel>();
            newServices.AddTransient<MainViewModel>();
            newServices.AddTransient<SessionEditorViewModel>();
            newServices.AddSingleton<IClientUiSettingsStore, UiSettingsStore>();
            newServices.AddTransient<SettingsViewModel>();

            var oldProvider = app.Services as IDisposable;
            var newProvider = newServices.BuildServiceProvider();
            // Intentionally keep the old provider alive so existing services like HostTrayService
            // continue to function correctly with their original service provider.
            app.Services = newProvider;
            logger.LogInformation("ServiceProvider recreated (old provider disposed)");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                loadingViewModel.Message = "Loading presets...";
                loadingViewModel.SubMessage = "Fetching session presets and module metadata...";
            });

            logger.LogInformation("Creating MainViewModel on UI thread...");
            var mainViewModel = await Dispatcher.UIThread.InvokeAsync(() =>
                app.Services.GetRequiredService<MainViewModel>());

            logger.LogInformation("Loading preset data...");
            await mainViewModel.InitializeAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                loadingViewModel.Message = "Ready";
                loadingViewModel.SubMessage = "Axorith Client is ready.";
            });

            await Dispatcher.UIThread.InvokeAsync(() => { shellViewModel.Content = mainViewModel; });

            logger.LogInformation("Starting host health monitoring...");
            var healthMonitor = app.Services.GetRequiredService<IHostHealthMonitor>();
            healthMonitor.HostUnhealthy += () =>
            {
                logger.LogWarning("Host became unhealthy - showing error page");
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var errorViewModel = app.Services.GetRequiredService<ErrorViewModel>();
                        errorViewModel.Configure(
                            "Lost connection to Axorith.Host.\n\nRestart the Host using the Axorith Client tray menu, then click 'Retry' to reconnect.",
                            async () =>
                            {
                                var loadingVm = new LoadingViewModel();
                                shellViewModel.Content = loadingVm;
                                await InitializeAsync(app, config, loggerFactory, logger);
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
            logger.LogError(ex, "Failed to connect/initialize Host");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var errorViewModel = app.Services.GetRequiredService<ErrorViewModel>();
                errorViewModel.Configure(
                    $"Initialization error: {ex.Message}\n\nIf Axorith.Host is not running, start or restart it using the Axorith Client tray menu, then click 'Retry' to try again.",
                    async () => { await InitializeAsync(app, config, loggerFactory, logger); });
                shellViewModel.Content = errorViewModel;
            });
        }
    }
}