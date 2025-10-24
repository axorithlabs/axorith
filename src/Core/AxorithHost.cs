using System.Diagnostics;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Axorith.Core.Http;
using Axorith.Core.Logging;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using IHttpClientFactory = Axorith.Sdk.Http.IHttpClientFactory;

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

        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Axorith",
            "logs", "axorith-.log");

        const string outputTemplate =
            "[{Timestamp:HH:mm:ss} {Level:u3}] " +
            "{ShortSourceContext}: " +
            "{ModuleContext}" +
            "{Message:lj}" +
            "{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.With<ShortSourceContextEnricher>()
            .Enrich.With<ModuleContextEnricher>()
            .WriteTo.Console(outputTemplate: outputTemplate)
            .WriteTo.Debug(outputTemplate: outputTemplate)
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, outputTemplate: outputTemplate)
            .CreateLogger();

        var hostLogger = Log.ForContext<AxorithHost>();

        hostLogger.Information("Starting up...");

        try
        {
            var hostBuilder = new HostBuilder()
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureServices(services =>
                {
                    services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(Log.Logger));
                    
                    services.AddHttpClient();
                })
                .ConfigureContainer<ContainerBuilder>(builder =>
                {
                    // Http Services
                    builder.RegisterType<HttpClientFactoryAdapter>().As<IHttpClientFactory>().SingleInstance();
                    
                    // Core Services
                    builder.RegisterType<ModuleLoader>().As<IModuleLoader>().SingleInstance();
                    builder.RegisterType<ModuleRegistry>().As<IModuleRegistry>().SingleInstance();
                    builder.RegisterType<PresetManager>().As<IPresetManager>().SingleInstance();
                    builder.RegisterType<SessionManager>().As<ISessionManager>().SingleInstance();
                });

            var host = hostBuilder.Build();

            var stepSw = Stopwatch.StartNew();
            hostLogger.Information("Initializing module registry...");

            if (host.Services.GetRequiredService<IModuleRegistry>() is ModuleRegistry moduleRegistry)
                await moduleRegistry.InitializeAsync(cancellationToken);

            stepSw.Stop();
            hostLogger.Information("Module registry initialized in {ElapsedMs} ms", stepSw.ElapsedMilliseconds);

            totalSw.Stop();
            hostLogger.Information("Created successfully in {ElapsedMs} ms total", totalSw.ElapsedMilliseconds);

            return new AxorithHost(host);
        }
        catch (Exception ex)
        {
            hostLogger.Fatal(ex, "A critical error occurred during host initialization");
            throw new HostInitializationException("Failed to initialize the Axorith Core. See logs for details.", ex);
        }
    }

    public void Dispose()
    {
        _host.Dispose();

        Log.CloseAndFlush();
    }
}
