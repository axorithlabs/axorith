using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Serilog;
using IHttpClientFactory = Axorith.Sdk.Http.IHttpClientFactory;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

// Host info file path for client discovery
var hostInfoPath = Path.Combine(Environment.ExpandEnvironmentVariables("%AppData%/Axorith"), "host-info.json");

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

    // Determine actual port to use (check if configured port is available)
    var config = builder.Configuration.Get<Configuration>() ?? new Configuration();
    var bindAddress = IPAddress.Parse(config.Grpc.BindAddress);
    var configuredPort = config.Grpc.Port;
    var actualPort = IsPortAvailable(bindAddress, configuredPort) ? configuredPort : 0;

    if (actualPort == 0)
    {
        Log.Warning("Configured port {Port} is busy, will use dynamic port assignment", configuredPort);
    }

    builder.WebHost.ConfigureKestrel((_, options) =>
    {
        options.Listen(bindAddress, actualPort, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
            // NO TLS for local MVP - listening on loopback only
        });

        options.Limits.Http2.MaxStreamsPerConnection = config.Grpc.MaxConcurrentStreams;
        options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(config.Grpc.KeepAliveInterval);
        options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(config.Grpc.KeepAliveTimeout);
    });

    builder.Services.AddSingleton<IHostAuthenticationService, HostAuthenticationService>();
    
    builder.Services.AddHostedService<NativeMessagingRegistrar>();

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
    
    _ = Task.Run(async () =>
    {
        try
        {
            var moduleRegistry = app.Services.GetRequiredService<IModuleRegistry>();
            if (moduleRegistry is ModuleRegistry concreteRegistry)
            {
                await concreteRegistry.InitializeAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
                Log.Information("ModuleRegistry initialized in background");
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception initEx)
        {
            Log.Warning(initEx, "ModuleRegistry initialization failed; continuing without modules");
        }
    }, app.Lifetime.ApplicationStopping);

    _ = Task.Run(async () =>
    {
        try
        {
            var scheduler = app.Services.GetRequiredService<IScheduleManager>();
            await scheduler.StartAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start ScheduleManager");
        }
    }, app.Lifetime.ApplicationStopping);

    _ = Task.Run(async () =>
    {
        try
        {
            var autoStopService = app.Services.GetRequiredService<ISessionAutoStopService>();
            await autoStopService.StartAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start SessionAutoStopService");
        }
    }, app.Lifetime.ApplicationStopping);

    app.MapGrpcService<PresetsServiceImpl>();
    app.MapGrpcService<SessionsServiceImpl>();
    app.MapGrpcService<ModulesServiceImpl>();
    app.MapGrpcService<DiagnosticsServiceImpl>();
    app.MapGrpcService<HostManagementServiceImpl>();
    app.MapGrpcService<SchedulerServiceImpl>();
    app.MapGrpcService<NotificationServiceImpl>();

    if (app.Environment.IsDevelopment())
    {
        app.MapGrpcReflectionService();
    }

    app.MapGet("/", () => "Axorith.Host gRPC server is running. Use gRPC client to connect.");

    await app.StartAsync();

    var server = app.Services.GetRequiredService<IServer>();
    var addressFeature = server.Features.Get<IServerAddressesFeature>();
    var boundPort = 0;

    if (addressFeature?.Addresses.FirstOrDefault() is { } address)
    {
        var uri = new Uri(address);
        boundPort = uri.Port;
    }
    else if (actualPort != 0)
    {
        boundPort = actualPort;
    }

    if (boundPort > 0)
    {
        try
        {
            var hostInfoDir = Path.GetDirectoryName(hostInfoPath);
            if (!string.IsNullOrEmpty(hostInfoDir) && !Directory.Exists(hostInfoDir))
            {
                Directory.CreateDirectory(hostInfoDir);
            }

            var hostInfo = new { port = boundPort, address = config.Grpc.BindAddress, timestamp = DateTimeOffset.UtcNow };
            await File.WriteAllTextAsync(hostInfoPath, JsonSerializer.Serialize(hostInfo));
            Log.Information("Host info written to {Path}", hostInfoPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write host-info.json - clients may not auto-discover dynamic port");
        }
    }
    else
    {
        Log.Warning("Could not determine bound port. Host info file will not be written.");
    }

    Log.Information("Axorith.Host started successfully on {Address}:{Port}",
        config.Grpc.BindAddress,
        boundPort > 0 ? boundPort : "Unknown");

    await app.WaitForShutdownAsync();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Axorith.Host terminated unexpectedly");
    return 1;
}
finally
{
    try
    {
        if (File.Exists(hostInfoPath))
        {
            File.Delete(hostInfoPath);
        }
    }
    catch
    {
        // Ignore cleanup errors
    }

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

    builder.Register(ctx =>
        {
            var logger = ctx.Resolve<ILogger<ISystemNotificationService>>();
            return PlatformServices.CreateNotificationService(logger);
        })
        .As<ISystemNotificationService>()
        .SingleInstance()
        .PreserveExistingDefaults();
    
    builder.Register(ctx =>
        {
            var loggerFactory = ctx.Resolve<ILoggerFactory>();
            return PlatformServices.CreateNativeMessagingManager(loggerFactory);
        })
        .As<INativeMessagingManager>()
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
            var autoStopService = ctx.Resolve<ISessionAutoStopService>();
            var notifier = ctx.Resolve<INotifier>();
            var logger = ctx.Resolve<ILogger<ScheduleManager>>();

            return new ScheduleManager(rootDataDir, sessionManager, presetManager, autoStopService, notifier, logger);
        })
        .As<IScheduleManager>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(ctx =>
        {
            var sessionManager = ctx.Resolve<ISessionManager>();
            var presetManager = ctx.Resolve<IPresetManager>();
            var notifier = ctx.Resolve<INotifier>();
            var logger = ctx.Resolve<ILogger<SessionAutoStopService>>();

            return new SessionAutoStopService(sessionManager, presetManager, notifier, logger);
        })
        .As<ISessionAutoStopService>()
        .SingleInstance()
        .PreserveExistingDefaults();
        
    builder.RegisterType<HostNotifier>()
        .As<INotifier>()
        .SingleInstance()
        .PreserveExistingDefaults();
}

static void RegisterBroadcasters(ContainerBuilder builder)
{
    builder.RegisterType<SessionEventBroadcaster>()
        .AsSelf()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.RegisterType<SettingUpdateBroadcaster>()
        .AsSelf()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.RegisterType<DesignTimeSandboxManager>()
        .As<IDesignTimeSandboxManager>()
        .AsSelf()
        .SingleInstance()
        .PreserveExistingDefaults();
        
    builder.RegisterType<NotificationBroadcaster>()
        .AsSelf()
        .SingleInstance()
        .PreserveExistingDefaults();
}

static bool IsPortAvailable(IPAddress address, int port)
{
    try
    {
        using var listener = new TcpListener(address, port);
        listener.Start();
        listener.Stop();
        return true;
    }
    catch (SocketException)
    {
        return false;
    }
}