using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Axorith.Client.ViewModels;
using Axorith.Client.Views;
using Axorith.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Axorith.Client;

/// <summary>
///     The main entry point for the Axorith client application.
///     This class is responsible for initializing the application, setting up dependency injection,
///     and creating the main window.
/// </summary>
public class App : Application
{
    /// <summary>
    ///     Gets the application's dependency injection service provider.
    /// </summary>
    public IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    ///     Loads the application's XAML resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Handles the application's startup logic after the Avalonia framework is ready.
    ///     This method is responsible for:
    ///     1. Asynchronously creating and configuring the AxorithHost (the application's core).
    ///     2. Setting up the dependency injection container with all necessary services and ViewModels.
    ///     3. Asynchronously initializing the main ViewModel with data.
    ///     4. Creating and displaying the main window with its shell ViewModel.
    ///     5. Registering a handler for graceful shutdown of the core host.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        try
        {
            // Create services synchronously to avoid race condition
            var services = new ServiceCollection();
            var host = AxorithHost.CreateAsync().GetAwaiter().GetResult();

            services.AddSingleton(host);
            services.AddSingleton(host.Sessions);
            services.AddSingleton(host.Presets);
            services.AddSingleton(host.Modules);
            services.AddSingleton<ShellViewModel>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<SessionEditorViewModel>();

            Services = services.BuildServiceProvider();

            // Create shell and window synchronously
            var shellViewModel = Services.GetRequiredService<ShellViewModel>();
            var mainViewModel = Services.GetRequiredService<MainViewModel>();

            shellViewModel.Content = mainViewModel;

            desktop.MainWindow = new MainWindow
            {
                DataContext = shellViewModel
            };

            // Register shutdown handler
            desktop.ShutdownRequested += async (_, e) =>
            {
                var hostService = Services.GetService<AxorithHost>();
                if (hostService is IAsyncDisposable asyncDisposable)
                {
                    e.Cancel = true;
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
                        lt.Shutdown();
                }
                else if (hostService is IDisposable disposable)
                {
                    e.Cancel = true;
                    disposable.Dispose();
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
                        lt.Shutdown();
                }
            };

            // Load data asynchronously after window is shown
            _ = mainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Critical initialization error: {ex}");
            Environment.Exit(1);
        }

        base.OnFrameworkInitializationCompleted();
    }
}