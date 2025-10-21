using System.Diagnostics;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Axorith.Core;

public sealed class AxorithHost : IDisposable
{
    private readonly IHost _host;

    private AxorithHost(IHost host)
    {
        _host = host;
    }

    public ILifetimeScope RootScope => _host.Services.GetRequiredService<ILifetimeScope>();
    public ISessionManager Sessions => _host.Services.GetRequiredService<ISessionManager>();
    public IPresetManager Presets => _host.Services.GetRequiredService<IPresetManager>();
    public IModuleRegistry Modules => _host.Services.GetRequiredService<IModuleRegistry>();

    public static async Task<AxorithHost> CreateAsync(CancellationToken cancellationToken = default)
    {
        var totalSw = Stopwatch.StartNew();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .CreateBootstrapLogger();

        Log.Information("AxorithHost starting up...");

        try
        {
            var hostBuilder = new HostBuilder()
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureContainer<ContainerBuilder>(builder =>
                {
                    builder.RegisterType<ModuleLoader>().As<IModuleLoader>().SingleInstance();
                    builder.RegisterType<ModuleRegistry>().As<IModuleRegistry>().SingleInstance();
                    builder.RegisterType<PresetManager>().As<IPresetManager>().SingleInstance();
                    builder.RegisterType<SessionManager>().As<ISessionManager>().SingleInstance();
                })
                .UseSerilog((context, services, configuration) =>
                {
                    // Здесь мы настраиваем Serilog для всего приложения
                    var logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Axorith", "logs", "axorith-.log");

                    configuration
                        .ReadFrom.Services(services)
                        .Enrich.FromLogContext()
                        .MinimumLevel.Debug()
                        .WriteTo.Console()
                        .WriteTo.Debug()
                        .WriteTo.File(logPath, rollingInterval: RollingInterval.Day);
                });

            var host = hostBuilder.Build();

            var stepSw = Stopwatch.StartNew();
            Log.Information("--> Initializing module registry...");

            if (host.Services.GetRequiredService<IModuleRegistry>() is ModuleRegistry moduleRegistry)
                await moduleRegistry.InitializeAsync(cancellationToken);

            stepSw.Stop();
            Log.Information("--> Module registry initialized in {ElapsedMs} ms", stepSw.ElapsedMilliseconds);

            totalSw.Stop();
            Log.Information("AxorithHost created successfully in {ElapsedMs} ms total.", totalSw.ElapsedMilliseconds);

            return new AxorithHost(host);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "A critical error occurred during host initialization.");
            throw new HostInitializationException("Failed to initialize the Axorith Core. See logs for details.", ex);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void ConfigureServices(IServiceCollection services, ILogger logger)
    {
        services.AddLogging(builder => builder.AddSerilog(logger, true));

        services.AddSingleton<IModuleLoader, ModuleLoader>();
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
        services.AddSingleton<IPresetManager, PresetManager>();
        services.AddSingleton<ISessionManager, SessionManager>();
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}