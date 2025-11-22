using System.Net;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Axorith.Core.Http;
using Axorith.Core.Logging;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Host;
using Axorith.Host.Interceptors;
using Axorith.Host.Services;
using Axorith.Host.Streaming;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Serilog;
using IHttpClientFactory = Axorith.Sdk.Http.IHttpClientFactory;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Axorith.Host...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, _, configuration) =>
    {
        var logsPath = context.Configuration.GetValue<string>("Persistence:LogsPath")
                       ?? "%AppData%/Axorith/logs";

        var resolvedLogsPath = Environment.ExpandEnvironmentVariables(logsPath);

        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.With<ShortSourceContextEnricher>()
            .Enrich.With<ModuleContextEnricher>()
            .WriteTo.File(
                Path.Combine(resolvedLogsPath, "host-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {ShortSourceContext}: {ModuleContext}{Message:lj}{NewLine}{Exception}");
    });

    builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

    builder.Services.Configure<Configuration>(builder.Configuration);

    builder.WebHost.ConfigureKestrel((context, options) =>
    {
        var config = context.Configuration.Get<Configuration>() ?? new Configuration();
        var bindAddress = IPAddress.Parse(config.Grpc.BindAddress);

        options.Listen(bindAddress, config.Grpc.Port, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
            // NO TLS for local MVP - listening on loopback only
        });

        options.Limits.Http2.MaxStreamsPerConnection = config.Grpc.MaxConcurrentStreams;
        options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(config.Grpc.KeepAliveInterval);
        options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(config.Grpc.KeepAliveTimeout);
    });

    builder.Services.AddSingleton<IHostAuthenticationService, HostAuthenticationService>();

    builder.Services.AddGrpc(options =>
    {
        options.MaxReceiveMessageSize = 16 * 1024 * 1024;
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();

        options.Interceptors.Add<AuthenticationInterceptor>();
    });

    builder.Services.AddHttpClient("default");

    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddGrpcReflection();
    }

    builder.Host.ConfigureContainer<ContainerBuilder>((_, containerBuilder) =>
    {
        RegisterCoreServices(containerBuilder);
        RegisterBroadcasters(containerBuilder);
    });

    var app = builder.Build();

    try
    {
        var authService = app.Services.GetRequiredService<IHostAuthenticationService>();
        authService.InitializeToken();
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to initialize authentication service. Host cannot start securely.");
        return 1;
    }

    try
    {
        var moduleRegistry = app.Services.GetRequiredService<IModuleRegistry>();
        if (moduleRegistry is ModuleRegistry concreteRegistry)
        {
            await concreteRegistry.InitializeAsync(CancellationToken.None);
        }
    }
    catch (Exception initEx)
    {
        Log.Warning(initEx, "ModuleRegistry initialization failed; continuing without modules");
    }

    // --- START SCHEDULER ---
    try
    {
        var scheduler = app.Services.GetRequiredService<IScheduleManager>();
        // Fire and forget start, it runs in background
        _ = scheduler.StartAsync(app.Lifetime.ApplicationStopping);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to start ScheduleManager");
    }
    // -----------------------

    app.MapGrpcService<PresetsServiceImpl>();
    app.MapGrpcService<SessionsServiceImpl>();
    app.MapGrpcService<ModulesServiceImpl>();
    app.MapGrpcService<DiagnosticsServiceImpl>();
    app.MapGrpcService<HostManagementServiceImpl>();

    // --- REGISTER NEW SERVICE ---
    app.MapGrpcService<SchedulerServiceImpl>();
    // ----------------------------

    if (app.Environment.IsDevelopment())
    {
        app.MapGrpcReflectionService();
    }

    app.MapGet("/", () => "Axorith.Host gRPC server is running. Use gRPC client to connect.");

    Log.Information("Axorith.Host started successfully on {Address}:{Port}",
        builder.Configuration.GetValue<string>("Grpc:BindAddress"),
        builder.Configuration.GetValue<int>("Grpc:Port"));

    await app.RunAsync();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Axorith.Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static void RegisterCoreServices(ContainerBuilder builder)
{
    builder.Register(ctx =>
        {
            var logger = ctx.Resolve<ILogger<ISecureStorageService>>();
            return PlatformServices.CreateSecureStorage(logger);
        })
        .As<ISecureStorageService>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(ctx =>
        {
            var loggerFactory = ctx.Resolve<ILoggerFactory>();
            return PlatformServices.CreateAppDiscoveryService(loggerFactory);
        })
        .As<IAppDiscoveryService>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.RegisterType<ModuleLoader>()
        .As<IModuleLoader>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(ctx =>
        {
            var config = ctx.Resolve<IOptions<Configuration>>().Value;
            var searchPaths = config.Modules.ResolveSearchPaths();
            var allowedSymlinks = config.Modules.AllowedSymlinks.Select(Environment.ExpandEnvironmentVariables);
            var rootScope = ctx.Resolve<ILifetimeScope>();
            var moduleLoader = ctx.Resolve<IModuleLoader>();
            var logger = ctx.Resolve<ILogger<ModuleRegistry>>();

            return new ModuleRegistry(rootScope, moduleLoader, searchPaths, allowedSymlinks, logger);
        })
        .As<IModuleRegistry>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.RegisterType<EventAggregator>()
        .As<IEventAggregator>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(ctx =>
            new HttpClientFactoryAdapter(
                ctx.Resolve<System.Net.Http.IHttpClientFactory>()))
        .As<IHttpClientFactory>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(ctx =>
        {
            var config = ctx.Resolve<IOptions<Configuration>>().Value;
            var presetsDirectory = config.Persistence.ResolvePresetsPath();
            var logger = ctx.Resolve<ILogger<PresetManager>>();

            return new PresetManager(presetsDirectory, logger);
        })
        .As<IPresetManager>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(ctx =>
        {
            var config = ctx.Resolve<IOptions<Configuration>>().Value;
            var moduleRegistry = ctx.Resolve<IModuleRegistry>();
            var logger = ctx.Resolve<ILogger<SessionManager>>();

            var validationTimeout = TimeSpan.FromSeconds(config.Session.ValidationTimeoutSeconds);
            var startupTimeout = TimeSpan.FromSeconds(config.Session.StartupTimeoutSeconds);
            var shutdownTimeout = TimeSpan.FromSeconds(config.Session.ShutdownTimeoutSeconds);

            return new SessionManager(moduleRegistry, logger, validationTimeout, startupTimeout, shutdownTimeout);
        })
        .As<ISessionManager>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(ctx =>
        {
            var config = ctx.Resolve<IOptions<Configuration>>().Value;

            var presetsPath = config.Persistence.ResolvePresetsPath();
            var rootDataDir = Directory.GetParent(presetsPath)?.FullName ?? Path.GetDirectoryName(presetsPath)!;

            var sessionManager = ctx.Resolve<ISessionManager>();
            var presetManager = ctx.Resolve<IPresetManager>();
            var logger = ctx.Resolve<ILogger<ScheduleManager>>();

            return new ScheduleManager(rootDataDir, sessionManager, presetManager, logger);
        })
        .As<IScheduleManager>()
        .SingleInstance()
        .PreserveExistingDefaults();
}

static void RegisterBroadcasters(ContainerBuilder builder)
{
    builder.RegisterType<SessionEventBroadcaster>()
        .AsSelf()
        .InstancePerLifetimeScope()
        .PreserveExistingDefaults();

    builder.RegisterType<SettingUpdateBroadcaster>()
        .AsSelf()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.RegisterType<DesignTimeSandboxManager>()
        .AsSelf()
        .SingleInstance()
        .PreserveExistingDefaults();
}