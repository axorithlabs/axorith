using Axorith.Core.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Diagnostics;
using Axorith.Core.Services;
using Axorith.Shared.Exceptions;
using Microsoft.Extensions.Hosting;

namespace Axorith.Core;

public sealed class AxorithHost : IDisposable
{
    private readonly IHost _host;

    private AxorithHost(IHost host)
    {
        _host = host;
    }

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
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IModuleLoader, ModuleLoader>();
                    services.AddSingleton<IModuleRegistry, ModuleRegistry>();
                    services.AddSingleton<IPresetManager, PresetManager>();
                    services.AddSingleton<ISessionManager, SessionManager>();
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

            // Асинхронная инициализация теперь делается здесь, после сборки хоста
            var stepSw = Stopwatch.StartNew();
            Log.Information("--> Initializing module registry...");
            
            // ModuleRegistry теперь должен быть IHostedService или мы его получаем и инициализируем вручную
            var moduleRegistry = host.Services.GetRequiredService<IModuleRegistry>() as ModuleRegistry;
            if (moduleRegistry != null)
            {
                await moduleRegistry.InitializeAsync(cancellationToken);
            }
            
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
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: true));
        
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