using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Axorith.Client.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Axorith.Client.Services;

public sealed class HostTrayService(
    HostController hostController,
    HostHealthMonitor healthMonitor,
    IOptions<Configuration> config,
    ILogger<HostTrayService> logger,
    IServiceProvider services)
{
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _hostStatusItem;
    private NativeMenuItem? _hostStartStopItem;
    private NativeMenuItem? _hostRestartItem;
    private NativeMenuItem? _showItem;
    private NativeMenuItem? _exitItem;
    private CancellationTokenSource? _cts;
    private bool _isHostRunning;
    private bool _canControlHost;
    private bool _isInitializing = true;
    private bool _isChangingHost;
    private IDisposable? _shellSubscription;
    private IDisposable? _sessionSubscription;

    public void Initialize(IClassicDesktopStyleApplicationLifetime desktop, ILogger<App> appLogger)
    {
        if (_trayIcon != null)
            return;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Axorith Client",
            Icon = new WindowIcon(iconPath),
            IsVisible = true
        };

        _showItem = new NativeMenuItem("Show") { IsEnabled = false };
        _showItem.Click += (_, _) => { ShowMainWindow(desktop); };

        _exitItem = new NativeMenuItem("Exit") { IsEnabled = false };
        _exitItem.Click += (_, _) =>
        {
            if (_isInitializing || _isChangingHost) return;
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
            _showItem,
            new NativeMenuItemSeparator()
        };

        _hostStatusItem = new NativeMenuItem("Host: Checking...") { IsEnabled = false };
        _hostStartStopItem = new NativeMenuItem("Start Host") { IsEnabled = false };
        _hostStartStopItem.Click += async (_, _) =>
        {
            if (_isInitializing || _isChangingHost || !_hostStartStopItem.IsEnabled) return;
            _isChangingHost = true;
            await UpdateMenuAsync();
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
            finally
            {
                _isChangingHost = false;
                await UpdateMenuAsync();
            }
        };

        _hostRestartItem = new NativeMenuItem("Restart Host") { IsEnabled = false };
        _hostRestartItem.Click += async (_, _) =>
        {
            if (_isInitializing || _isChangingHost || !_hostRestartItem.IsEnabled) return;
            _isChangingHost = true;
            await UpdateMenuAsync();
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
                _isChangingHost = false;
                await UpdateMenuAsync();
            }
        };

        menu.Add(_hostStatusItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_hostStartStopItem);
        menu.Add(_hostRestartItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_exitItem);

        _trayIcon.Menu = menu;

        if (services.GetService<ShellViewModel>() is { } shell)
        {
            _shellSubscription = shell.WhenAnyValue(vm => vm.Content).Subscribe(content =>
            {
                _sessionSubscription?.Dispose();
                _sessionSubscription = null;
                _isInitializing = content is null or LoadingViewModel;
                _ = UpdateMenuAsync();
                if (content is not MainViewModel main) return;
                _sessionSubscription = main.WhenAnyValue(vm => vm.IsCommittedSession).Subscribe(committed =>
                {
                    _canControlHost = !committed;
                    _ = UpdateMenuAsync();
                });
            });
        }

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
            var isReady = !_isInitializing && !_isChangingHost;
            if (_showItem != null) _showItem.IsEnabled = isReady;
            if (_exitItem != null) _exitItem.IsEnabled = isReady;
            _hostStatusItem?.Header = _isHostRunning ? "Host: Running" : "Host: Stopped";
            _hostStartStopItem?.Header = _isHostRunning ? "Stop Host" : "Start Host";
            if (_hostStartStopItem != null) _hostStartStopItem.IsEnabled = isReady && (!_isHostRunning || _canControlHost);
            if (_hostRestartItem != null) _hostRestartItem.IsEnabled = isReady && _isHostRunning && _canControlHost;
        });
    }

    private void ShowErrorPageImmediate()
    {
        try
        {
            var shell = services.GetService<ShellViewModel>();
            var errorVm = services.GetService<ErrorViewModel>();

            if (shell == null || errorVm == null)
                return;

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

                    var initializer = services.GetRequiredService<ConnectionInitializer>();
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
        if (_isInitializing || _isChangingHost) return;
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
        _shellSubscription?.Dispose();
        _sessionSubscription?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _trayIcon?.Dispose();
    }
}
