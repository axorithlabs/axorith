using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Axorith.Contracts;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Axorith.Service;

internal class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        var config = configBuilder.Build();

        var logsPath = config["Persistence:LogsPath"]
                       ?? "%AppData%/Axorith/logs";
        var resolvedLogsPath = Environment.ExpandEnvironmentVariables(logsPath);

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(resolvedLogsPath, "service-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Axorith Service starting (Avalonia)...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}

public class App : Application
{
    private bool _isHostRunning;
    private NativeMenuItem? _statusItem;
    private NativeMenuItem? _startStopItem;
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _trayIcon = new TrayIcon
            {
                ToolTipText = "Axorith Service",
                Icon = new WindowIcon("Assets/icon.png"),
                IsVisible = true
            };

            BuildMenu();

            var trayIcons = new TrayIcons { _trayIcon };
            SetValue(TrayIcon.IconsProperty, trayIcons);

            Log.Information("Tray icon created");

            _ = Task.Run(RefreshStatusAsync);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildMenu()
    {
        if (_trayIcon == null) return;

        var menu = new NativeMenu
        {
            new NativeMenuItem("Axorith Service") { IsEnabled = false },
            new NativeMenuItemSeparator()
        };

        _statusItem = new NativeMenuItem("Status: Checking...") { IsEnabled = false };
        menu.Add(_statusItem);

        menu.Add(new NativeMenuItemSeparator());

        _startStopItem = new NativeMenuItem("Start Host");
        _startStopItem.Click += async (_, _) =>
        {
            if (_isHostRunning)
                await StopHostAsync();
            else
                await StartHostAsync();
            await RefreshStatusAsync();
        };
        menu.Add(_startStopItem);

        var restartItem = new NativeMenuItem("Restart Host");
        restartItem.Click += async (_, _) =>
        {
            await StopHostAsync();
            await Task.Delay(1000);
            await StartHostAsync();
            await RefreshStatusAsync();
        };
        menu.Add(restartItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += async (_, _) =>
        {
            Log.Information("Exit requested");
            await StopHostAsync();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        };
        menu.Add(exitItem);

        _trayIcon.Menu = menu;
    }

    private async Task RefreshStatusAsync()
    {
        while (true)
        {
            try
            {
                _isHostRunning = await IsHostReachableAsync();
                // Marshal UI update to UI thread
                await Dispatcher.UIThread.InvokeAsync(UpdateMenu);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Status check failed");
            }

            await Task.Delay(5000);
        }
    }

    private void UpdateMenu()
    {
        _statusItem?.Header = _isHostRunning ? "Status: Host Running" : "Status: Host Stopped";

        _startStopItem?.Header = _isHostRunning ? "Stop Host" : "Start Host";
    }

    private static async Task<bool> IsHostReachableAsync()
    {
        try
        {
            using var channel = GrpcChannel.ForAddress("http://127.0.0.1:5901");
            var diagnostics = new DiagnosticsService.DiagnosticsServiceClient(channel);
            var response =
                await diagnostics.GetHealthAsync(new HealthCheckRequest(), deadline: DateTime.UtcNow.AddSeconds(2));
            return response.Status == HealthStatus.Healthy;
        }
        catch
        {
            return false;
        }
    }

    private async Task StartHostAsync()
    {
        var hostExe = FindExecutable("Axorith.Host.exe");
        if (hostExe == null)
        {
            Log.Error("Host not found");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = hostExe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(hostExe) ?? AppContext.BaseDirectory
            });
            Log.Information("Host started");
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Host");
        }
    }

    private static async Task StopHostAsync()
    {
        try
        {
            using var channel = GrpcChannel.ForAddress("http://127.0.0.1:5901");
            var management = new HostManagement.HostManagementClient(channel);
            await management.RequestShutdownAsync(new ShutdownRequest { Reason = "Service stop", TimeoutSeconds = 10 },
                deadline: DateTime.UtcNow.AddSeconds(5));
            Log.Information("Shutdown requested");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Graceful shutdown failed");
            foreach (var p in Process.GetProcessesByName("Axorith.Host"))
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignored
                }
        }
    }

    private static string? FindExecutable(string fileName)
    {
        var probes = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "../Axorith.Host", fileName)
        };

        foreach (var path in probes.Select(Path.GetFullPath))
            if (File.Exists(path))
                return path;

        return null;
    }
}