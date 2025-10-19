using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Axorith.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Axorith.Client;

/// <summary>
/// The main entry point for the Axorith client application.
/// This class is responsible for initializing the application, setting up dependency injection,
/// and creating the main window.
/// </summary>
public class App : Application
{
    /// <summary>
    /// Gets the application's dependency injection service provider.
    /// </summary>
    public IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Loads the application's XAML resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Handles the application's startup logic after the Avalonia framework is ready.
    /// This method is responsible for:
    /// 1. Asynchronously creating and configuring the AxorithHost (the application's core).
    /// 2. Setting up the dependency injection container with all necessary services and ViewModels.
    /// 3. Asynchronously initializing the main ViewModel with data.
    /// 4. Creating and displaying the main window with its shell ViewModel.
    /// 5. Registering a handler for graceful shutdown of the core host.
    /// </summary>
    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        await ConfigureServicesAsync(services);
        Services = services.BuildServiceProvider();

        // Resolve the main shell and the initial page (dashboard)
        var shellViewModel = Services.GetRequiredService<ShellViewModel>();
        var mainViewModel = Services.GetRequiredService<MainViewModel>();
        
        // Load initial data for the dashboard before showing it
        await mainViewModel.InitializeAsync();
        
        // Set the initial page in the shell
        shellViewModel.Content = mainViewModel;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = shellViewModel,
            };

            // Ensure the core host is properly disposed of when the application closes
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

    /// <summary>
    /// Configures the dependency injection container for the application.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    private async Task ConfigureServicesAsync(IServiceCollection services)
    {
        // Asynchronously create the core host, which handles all business logic.
        var host = await AxorithHost.CreateAsync();
        
        // Register the host and its services as singletons.
        services.AddSingleton(host);
        services.AddSingleton(host.Sessions);
        services.AddSingleton(host.Presets);
        services.AddSingleton(host.Modules);

        // Register ViewModels. The Shell is a singleton, pages are transient.
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<SessionEditorViewModel>();
    }
}