using Axorith.Core.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Diagnostics;
using Axorith.Core.Services;
using Axorith.Shared.Exceptions;

namespace Axorith.Core;

public sealed class AxorithHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    private AxorithHost(ServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ISessionManager Sessions => _serviceProvider.GetRequiredService<ISessionManager>();
    public IPresetManager Presets => _serviceProvider.GetRequiredService<IPresetManager>();
    public IModuleRegistry Modules => _serviceProvider.GetRequiredService<IModuleRegistry>();

    public static async Task<AxorithHost> CreateAsync(CancellationToken cancellationToken = default)
    {
        var totalSw = Stopwatch.StartNew();
        var logger = Logging.CreateLogger();
    
        try
        {
            logger.Information("AxorithHost starting up...");
            var stepSw = Stopwatch.StartNew();

            var services = new ServiceCollection();
            ConfigureServices(services, logger);
            stepSw.Stop();
            logger.Information("--> Service configuration finished in {ElapsedMs} ms", stepSw.ElapsedMilliseconds);

            stepSw.Restart();
            var serviceProvider = services.BuildServiceProvider();
            stepSw.Stop();
            logger.Information("--> DI container built in {ElapsedMs} ms", stepSw.ElapsedMilliseconds);

            stepSw.Restart();

            if (serviceProvider.GetRequiredService<IModuleRegistry>() is ModuleRegistry moduleRegistry)
            {
                await moduleRegistry.InitializeAsync(cancellationToken);
            }
            
            stepSw.Stop();
            logger.Information("--> Module registry initialized in {ElapsedMs} ms", stepSw.ElapsedMilliseconds);

            var host = new AxorithHost(serviceProvider);
        
            totalSw.Stop();
            logger.Information("AxorithHost created successfully in {ElapsedMs} ms total.", totalSw.ElapsedMilliseconds);

            return host;
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "A critical error occurred during host initialization. The application cannot start.");
            
            throw new HostInitializationException("Failed to initialize the Axorith Core. See logs for details.", ex);
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
        Log.CloseAndFlush();
        _serviceProvider.Dispose();
    }
}