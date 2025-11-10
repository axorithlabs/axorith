using System.Diagnostics;
using System.Runtime.InteropServices;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Axorith.Core.Http;
using Axorith.Core.Logging;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk.Services;
using Axorith.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Serilog;
using Serilog.Events;
using IHttpClientFactory = Axorith.Sdk.Http.IHttpClientFactory;

namespace Axorith.Core;

public sealed class AxorithHost : IDisposable, IAsyncDisposable
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

        // Resolve logs path from environment variable (fallback to default)
        var logsPath = Environment.GetEnvironmentVariable("AXORITH_LOGS_PATH")
                       ?? Path.Combine(
                           Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                           "Axorith", "logs");
        var logPath = Path.Combine(logsPath, "axorith-.log");

        const string outputTemplate =
            "[{Timestamp:HH:mm:ss} {Level:u3}] " +
            "{ShortSourceContext}: " +
            "{ModuleContext}" +
            "{Message:lj}" +
            "{NewLine}{Exception}";

        var isDebug = Debugger.IsAttached;
        var minLevel = isDebug ? LogEventLevel.Debug : LogEventLevel.Information;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .Enrich.With<ShortSourceContextEnricher>()
            .Enrich.With<ModuleContextEnricher>()
            .WriteTo.Console(outputTemplate: outputTemplate)
            .WriteTo.Debug(outputTemplate: outputTemplate)
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB per file
                retainedFileCountLimit: 30, // Keep last 30 files
                rollOnFileSizeLimit: true,
                outputTemplate: outputTemplate)
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

                    // HTTP Client for modules with retry and timeout policies
                    // NOTE: Circuit breaker intentionally NOT added to default client to prevent
                    // one module's failures from affecting others. Modules should use named clients
                    // if they need isolated circuit breakers.
                    services.AddHttpClient("default")
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
                        .AddPolicyHandler(GetRetryPolicy())
                        .AddPolicyHandler(GetTimeoutPolicy());
                })
                .ConfigureContainer<ContainerBuilder>(builder =>
                {
                    // Http Services
                    builder.RegisterType<HttpClientFactoryAdapter>().As<IHttpClientFactory>().SingleInstance();

                    // Secure Storage services - platform-specific
                    builder.Register(ctx =>
                    {
                        var loggerFactory = ctx.Resolve<ILoggerFactory>();
                        var logger = loggerFactory.CreateLogger("ISecureStorageService");
                        return Axorith.Shared.Platform.PlatformServices.CreateSecureStorage(logger);
                    }).As<ISecureStorageService>().SingleInstance();

                    // Core Services
                    builder.RegisterType<ModuleLoader>().As<IModuleLoader>().SingleInstance();
                    builder.RegisterType<ModuleRegistry>().As<IModuleRegistry>().SingleInstance();
                    builder.RegisterType<PresetManager>().As<IPresetManager>().SingleInstance();
                    builder.RegisterType<SessionManager>().As<ISessionManager>().SingleInstance();

                    // Event Aggregator
                    builder.RegisterType<EventAggregator>().As<IEventAggregator>().SingleInstance();
                });

            var host = hostBuilder.Build();

            var stepSw = Stopwatch.StartNew();
            hostLogger.Information("Initializing module registry...");

            if (host.Services.GetRequiredService<IModuleRegistry>() is ModuleRegistry moduleRegistry)
                await moduleRegistry.InitializeAsync(cancellationToken).ConfigureAwait(false);

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

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        _host.Dispose();

        Log.CloseAndFlush();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is IAsyncDisposable hostAsyncDisposable)
            await hostAsyncDisposable.DisposeAsync();
        else
            _host.Dispose();

        await Log.CloseAndFlushAsync();
    }
}