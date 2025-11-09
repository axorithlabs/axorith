using Avalonia;
using Avalonia.Controls;
using ReactiveUI.Avalonia;

namespace Axorith.Client;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = BuildAvaloniaApp();
        
        // Tray icon is always visible, but --tray hides window on startup
        // Use OnExplicitShutdown to prevent closing app when window is closed
        app.StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
    }
}