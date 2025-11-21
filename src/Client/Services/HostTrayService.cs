using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Axorith.Client.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axorith.Client.Services;

public interface IHostTrayService : IDisposable
{
    void Initialize(IClassicDesktopStyleApplicationLifetime desktop, ILogger<App> logger);
}

public sealed class HostTrayService(
    IHostController hostController,
    IHostHealthMonitor healthMonitor,
    IOptions<Configuration> config,
    ILogger<HostTrayService> logger,
    IServiceProvider services)
    : IHostTrayService
{
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _hostStatusItem;
    private NativeMenuItem? _hostStartStopItem;
    private NativeMenuItem? _hostRestartItem;
    private CancellationTokenSource? _cts;
    private bool _isHostRunning;

    public void Initialize(IClassicDesktopStyleApplicationLifetime desktop, ILogger<App> appLogger)
    {
        if (_trayIcon != null)
        {
            return;
        }

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Axorith Client",
            Icon = new WindowIcon("Assets/icon.ico"),
            IsVisible = true
        };

        var showItem = new NativeMenuItem("Show");
        showItem.Click += (_, _) => { ShowMainWindow(desktop); };

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            appLogger.LogInformation("Exit requested from tray");
            try
            {
                desktop.Shutdown();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tray exit failed");
            }
        };

        var menu = new NativeMenu
        {
            new NativeMenuItem("Axorith Client") { IsEnabled = false },
            new NativeMenuItemSeparator(),
            showItem,
            new NativeMenuItemSeparator()
        };

        _hostStatusItem = new NativeMenuItem("Host: Checking...") { IsEnabled = false };
        _hostStartStopItem = new NativeMenuItem("Start Host");
        _hostStartStopItem.Click += async (_, _) =>
        {
            try
            {
                if (_isHostRunning)
                {
                    healthMonitor.Pause();

                    await hostController.StopHostAsync();
                    _isHostRunning = false;
                    await UpdateMenuAsync();
                    ShowErrorPageImmediate();
                }
                else
                {
                    await hostController.StartHostAsync();
                    _isHostRunning = true;
                    await UpdateMenuAsync();

                    healthMonitor.Resume();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Host start/stop failed from tray");
                healthMonitor.Resume();
            }
        };

        _hostRestartItem = new NativeMenuItem("Restart Host");
        _hostRestartItem.Click += async (_, _) =>
        {
            try
            {
                healthMonitor.Pause();

                await hostController.RestartHostAsync();
                await UpdateMenuAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Host restart failed from tray");
            }
            finally
            {
                healthMonitor.Resume();
            }
        };

        menu.Add(_hostStatusItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_hostStartStopItem);
        menu.Add(_hostRestartItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);

        _trayIcon.Menu = menu;

        _trayIcon.Clicked += (_, _) => { ShowMainWindow(desktop); };

        var trayIcons = new TrayIcons { _trayIcon };
        Application.Current?.SetValue(TrayIcon.IconsProperty, trayIcons);

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => MonitorAsync(_cts.Token));
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var healthy = await hostController.IsHostReachableAsync(ct);
                _isHostRunning = healthy;
                await UpdateMenuAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Host tray monitor iteration failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, config.Value.Host.HealthCheckInterval)), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task UpdateMenuAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _hostStatusItem?.Header = _isHostRunning ? "Host: Running" : "Host: Stopped";
            _hostStartStopItem?.Header = _isHostRunning ? "Stop Host" : "Start Host";
        });
    }

    private void ShowErrorPageImmediate()
    {
        try
        {
            var shell = services.GetService<ShellViewModel>();
            var errorVm = services.GetService<ErrorViewModel>();

            if (shell == null || errorVm == null)
            {
                return;
            }

            errorVm.Configure(
                "Axorith.Host has been stopped from the tray.\n\nUse 'Start Host' from the tray menu or click 'Retry Connection' below to restart.",
                async () =>
                {
                    await hostController.StartHostAsync();

                    healthMonitor.Resume();

                    if (Application.Current is not App app)
                    {
                        throw new InvalidOperationException("Application.Current is not Axorith.Client.App");
                    }

                    var initializer = services.GetRequiredService<IConnectionInitializer>();
                    var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                    var appLogger = services.GetRequiredService<ILogger<App>>();
                    await initializer.InitializeAsync(app, config.Value, loggerFactory, appLogger);
                });

            Dispatcher.UIThread.Post(() => shell.Content = errorVm);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to show immediate error page after host stop");
        }
    }

    private void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            if (desktop.MainWindow != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        desktop.MainWindow.ShowInTaskbar = true;
                        desktop.MainWindow.WindowState = WindowState.Normal;
                        desktop.MainWindow.Activate();
                        desktop.MainWindow.BringIntoView();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to bring window to front from tray");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Show window failed");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}