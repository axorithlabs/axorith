using System.Net;
using System.Runtime.InteropServices;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Axorith.Core.Http;
using Axorith.Core.Logging;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Host;
using Axorith.Host.Services;
using Axorith.Host.Streaming;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform.Windows;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using IHttpClientFactory = Axorith.Sdk.Http.IHttpClientFactory;

// Bootstrap logger for startup
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Axorith.Host...");

    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog from appsettings.json
    builder.Host.UseSerilog((context, services, configuration) =>
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

    // Configure Autofac as DI container
    builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

    // Configure host configuration model
    builder.Services.Configure<HostConfiguration>(builder.Configuration);

    // Configure Kestrel for gRPC
    builder.WebHost.ConfigureKestrel((context, options) =>
    {
        var config = context.Configuration.Get<HostConfiguration>() ?? new HostConfiguration();
        var bindAddress = IPAddress.Parse(config.Grpc.BindAddress);

        options.Listen(bindAddress, config.Grpc.Port, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
            // NO TLS for local MVP - listening on loopback only
        });

        options.Limits.MaxConcurrentConnections = config.Grpc.MaxConcurrentStreams;
        options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(config.Grpc.KeepAliveTimeout);
    });

    // Add gRPC services
    builder.Services.AddGrpc(options =>
    {
        options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB for large presets
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    });

    // Add HttpClientFactory for module HTTP adapters
    builder.Services.AddHttpClient("default");

    // Add gRPC reflection for development (useful for testing with grpcurl)
    if (builder.Environment.IsDevelopment()) builder.Services.AddGrpcReflection();

    // Configure Autofac container
    builder.Host.ConfigureContainer<ContainerBuilder>((context, containerBuilder) =>
    {
        RegisterCoreServices(containerBuilder, context.Configuration);
        RegisterBroadcasters(containerBuilder);
    });

    var app = builder.Build();

    // Initialize ModuleRegistry before serving requests
    try
    {
        var moduleRegistry = app.Services.GetRequiredService<IModuleRegistry>();
        if (moduleRegistry is ModuleRegistry concreteRegistry)
            await concreteRegistry.InitializeAsync(CancellationToken.None);
    }
    catch (Exception initEx)
    {
        Log.Warning(initEx, "ModuleRegistry initialization failed; continuing without modules");
    }

    // Map gRPC services
    app.MapGrpcService<PresetsServiceImpl>();
    app.MapGrpcService<SessionsServiceImpl>();
    app.MapGrpcService<ModulesServiceImpl>();
    app.MapGrpcService<DiagnosticsServiceImpl>();
    app.MapGrpcService<HostManagementServiceImpl>();

    if (app.Environment.IsDevelopment()) app.MapGrpcReflectionService();

    app.MapGet("/", () => "Axorith.Host gRPC server is running. Use gRPC client to connect.");

    Log.Information("Axorith.Host started successfully on {Address}:{Port}",
        builder.Configuration.GetValue<string>("Grpc:BindAddress"),
        builder.Configuration.GetValue<int>("Grpc:Port"));

    // Run server (blocks until shutdown)
    Log.Information("Axorith.Host ready on http://127.0.0.1:5901");
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

// ===== Service Registration Methods =====

static void RegisterCoreServices(ContainerBuilder builder, IConfiguration configuration)
{
    // Register Core services (mirror AxorithHost.cs logic)

    // ISecureStorageService - platform specific
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        builder.RegisterType<SecureStorage>()
            .As<ISecureStorageService>()
            .SingleInstance();
    else
        // TODO: Add Linux/macOS implementations
        throw new PlatformNotSupportedException("Secure storage not implemented for this platform");

    // Module services
    builder.RegisterType<ModuleLoader>()
        .As<IModuleLoader>()
        .SingleInstance();

    builder.RegisterType<ModuleRegistry>()
        .As<IModuleRegistry>()
        .SingleInstance();

    // SDK EventAggregator - use Core implementation
    builder.RegisterType<EventAggregator>()
        .As<IEventAggregator>()
        .SingleInstance();

    // Expose SDK IHttpClientFactory via adapter wrapping System.Net HttpClientFactory
    builder.Register(ctx =>
            new HttpClientFactoryAdapter(
                ctx.Resolve<System.Net.Http.IHttpClientFactory>()))
        .As<IHttpClientFactory>()
        .SingleInstance();

    // Preset manager
    builder.RegisterType<PresetManager>()
        .As<IPresetManager>()
        .SingleInstance();

    // Session manager
    builder.RegisterType<SessionManager>()
        .As<ISessionManager>()
        .SingleInstance();
}

static void RegisterBroadcasters(ContainerBuilder builder)
{
    builder.RegisterType<SessionEventBroadcaster>()
        .AsSelf()
        .SingleInstance();

    builder.RegisterType<SettingUpdateBroadcaster>()
        .AsSelf()
        .SingleInstance();
}