using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Axorith.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Axorith.Client;

public class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Handles the application's startup logic after the Avalonia framework is ready.
    /// This method is responsible for:
    /// 1. Creating and configuring the AxorithHost (the application's core).
    /// 2. Setting up the dependency injection container with all necessary services.
    /// 3. Initializing and displaying the main window with its ViewModel.
    /// 4. Registering a handler for graceful shutdown.
    /// </summary>
    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        await ConfigureServicesAsync(services);
        Services = services.BuildServiceProvider();

        var shellViewModel = Services.GetRequiredService<ShellViewModel>();
        var mainViewModel = Services.GetRequiredService<MainViewModel>();
        
        await mainViewModel.InitializeAsync();
        
        shellViewModel.Content = mainViewModel;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = shellViewModel,
            };

            desktop.ShutdownRequested += (sender, args) =>
            {
                if (Services.GetService<AxorithHost>() is IDisposable host)
                {
                    host.Dispose();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task ConfigureServicesAsync(IServiceCollection services)
    {
        var host = await AxorithHost.CreateAsync();
        services.AddSingleton(host);
        services.AddSingleton(host.Sessions);
        services.AddSingleton(host.Presets);
        services.AddSingleton(host.Modules);

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<SessionEditorViewModel>();
    }
}