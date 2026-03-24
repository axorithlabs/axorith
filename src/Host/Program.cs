using System.Diagnostics;
using System.Text.Json;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Axorith.Core.Logging;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Host;
using Axorith.Host.Grpc;
using Axorith.Host.Interceptors;
using Axorith.Host.Services;
using Axorith.Host.Streaming;
using Axorith.Sdk.Services;
using Axorith.Shared.Licensing;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var hostInfoPath = ApplicationPaths.HostInfoFile;
ITelemetryService? telemetry = null;
var telemetryLogLevel = LogEventLevel.Warning;
var hostUptime = Stopwatch.StartNew();

// CRITICAL: Use global mutex to prevent multiple Host instances
// This protects against race conditions when multiple Clients start simultaneously
using var hostMutex = new Mutex(true, "Global\\AxorithHostInstanceMutex", out var createdNew);

if (!createdNew)
{
    Log.Warning("Another Axorith.Host instance is already running. Exiting.");

    // Check if the other instance is actually responsive
    await Task.Delay(1000);

    // Try to read existing host info
    if (File.Exists(hostInfoPath))
    {
        try
        {
            var existingInfo = await File.ReadAllTextAsync(hostInfoPath);
            Log.Information("Existing Host info: {Info}", existingInfo);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read existing host-info.json");
        }
    }

    Log.Information("Exiting duplicate Host instance.");
    return 0;
}

Log.Information("✅ Acquired Host instance mutex. This is the primary Host instance.");

try
{
    Log.Information("Starting Axorith.Host...");

    var builder = WebApplication.CreateBuilder(args);
    var telemetrySettings = new TelemetrySettings()
            .WithEnvironmentOverrides() with
        {
            ApplicationName = "Axorith.Host"
        };

    telemetryLogLevel = TelemetrySettings.ResolveLogLevel(telemetrySettings.LogLevel);
    telemetry = new TelemetryService(telemetrySettings);
    RegisterGlobalExceptionHandlers(telemetry);

    Log.Information(
        "Telemetry (Host): enabled={Enabled}, active={Active}, isEnabled={IsEnabled}, host={Host}, batch={Batch}, queue={Queue}, flushSec={Flush}",
        telemetrySettings.Enabled,
        telemetrySettings.IsActive,
        telemetry?.IsEnabled,
        telemetrySettings.PostHogHost,
        telemetrySettings.BatchSize,
        telemetrySettings.QueueLimit,
        telemetrySettings.FlushInterval.TotalSeconds);

    if (!telemetrySettings.IsActive)
    {
        Log.Warning(
            "Telemetry is INACTIVE. Reasons: Enabled={Enabled}, ApiKeyIsPlaceholder={IsPlaceholder}, ApiKeyEmpty={IsEmpty}, HostEmpty={HostEmpty}",
            telemetrySettings.Enabled,
            !string.IsNullOrWhiteSpace(telemetrySettings.PostHogApiKey) &&
            telemetrySettings.PostHogApiKey.StartsWith("##", StringComparison.Ordinal),
            string.IsNullOrWhiteSpace(telemetrySettings.PostHogApiKey),
            string.IsNullOrWhiteSpace(telemetrySettings.PostHogHost));
        Log.Information("To enable telemetry, set AXORITH_TELEMETRY_API_KEY environment variable");
    }
    else
    {
        telemetry?.TrackEvent("HostStarted");
        Log.Information("Telemetry event sent: HostStarted");
    }

    builder.Host.UseSerilog((context, _, configuration) =>
    {
        var logsPath = context.Configuration.GetValue<string>("Persistence:LogsPath");
        var resolvedLogsPath = string.IsNullOrWhiteSpace(logsPath)
            ? ApplicationPaths.Logs
            : ApplicationPaths.ExpandPath(logsPath);

        configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.With<ShortSourceContextEnricher>()
            .Enrich.With<ModuleContextEnricher>()
            .Filter.ByExcluding(e =>
                e.Properties.TryGetValue("SourceContext", out var sc) &&
                sc.ToString().StartsWith("\"Grpc.AspNetCore.Server", StringComparison.Ordinal))
            .WriteTo.File(
                Path.Combine(resolvedLogsPath, "host-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {ShortSourceContext}: {ModuleContext}{Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(new TelemetrySerilogSink(telemetry ?? new NoopTelemetryService()),
                restrictedToMinimumLevel: telemetryLogLevel);
    });

    builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

    builder.Services.AddSingleton(_ => telemetry ?? new NoopTelemetryService());
    builder.Services.AddSingleton(hostUptime);
    builder.Services.AddSingleton<IUserRegistrationService, UserRegistrationService>();
    builder.Services.AddSingleton<UpdateService>();
    builder.Services.Configure<Configuration>(builder.Configuration);

    var config = builder.Configuration.Get<Configuration>() ?? new Configuration();

    // Resolve IPC endpoint for local communication (Unix Domain Socket / Named Pipe)
    var ipcEndpoint = config.Grpc.ResolveIpcEndpoint();
    EnsureIpcDirectoryExists(ipcEndpoint);

    builder.WebHost.ConfigureKestrel((_, options) =>
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows: Use Named Pipes for local IPC
            options.ListenNamedPipe(ipcEndpoint, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        }
        else
        {
            // Linux/macOS: Use Unix Domain Sockets for local IPC
            // Remove stale socket file if it exists (from previous crash)
            if (File.Exists(ipcEndpoint))
            {
                try { File.Delete(ipcEndpoint); }
                catch (Exception ex) { Log.Warning(ex, "Failed to delete stale socket file: {Path}", ipcEndpoint); }
            }

            options.ListenUnixSocket(ipcEndpoint, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        }

        options.Limits.Http2.MaxStreamsPerConnection = config.Grpc.MaxConcurrentStreams;
        options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(config.Grpc.KeepAliveInterval);
        options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(config.Grpc.KeepAliveTimeout);
    });

    builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(1));

    builder.Services.AddSingleton(sp =>
        PlatformServices.CreateFilePermissionsService(sp.GetRequiredService<ILoggerFactory>()));
    builder.Services.AddSingleton<IHostAuthenticationService, HostAuthenticationService>();

    builder.Services.AddHostedService<NativeMessagingRegistrar>();

    builder.Services.AddGrpc(options =>
    {
        options.MaxReceiveMessageSize = 16 * 1024 * 1024;
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();

        // Version interceptor runs BEFORE authentication — fail fast on version mismatch
        options.Interceptors.Add<VersionInterceptor>();
        options.Interceptors.Add<AuthenticationInterceptor>();
    });

    builder.Services.AddHttpClient("default");

    builder.Services.AddHostedService<TelemetryHeartbeatService>();

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

    // Initialize user registration (for future licensing)
    _ = Task.Run(async () =>
    {
        try
        {
            var registrationService = app.Services.GetRequiredService<IUserRegistrationService>();
            var registration = await registrationService.GetOrCreateAsync(app.Lifetime.ApplicationStopping)
                .ConfigureAwait(false);

            Log.Information("User registration initialized: MachineId={MachineId}, FirstSeen={FirstSeen}",
                registration.MachineId[..8] + "...",
                registration.FirstSeenUtc);

            telemetry?.TrackEvent("UserRegistrationLoaded", new Dictionary<string, object?>
            {
                ["firstSeenUtc"] = registration.FirstSeenUtc.ToString("O"),
                ["appVersion"] = registration.AppVersion
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize user registration");
        }
    }, app.Lifetime.ApplicationStopping);

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
    app.MapGrpcService<GrpcUpdatesService>();
    app.MapGrpcService<PresenceServiceImpl>();

    if (app.Environment.IsDevelopment())
    {
        app.MapGrpcReflectionService();
    }

    app.MapGet("/", () => "Axorith.Host gRPC server is running. Use gRPC client to connect.");

    await app.StartAsync();

    // Write IPC endpoint info to host-info.json for client discovery
    try
    {
        var hostInfoDir = Path.GetDirectoryName(hostInfoPath);
        if (!string.IsNullOrEmpty(hostInfoDir) && !Directory.Exists(hostInfoDir))
        {
            Directory.CreateDirectory(hostInfoDir);
        }

        var hostInfo = new { ipcEndpoint = ipcEndpoint, timestamp = DateTimeOffset.UtcNow };

        var json = JsonSerializer.Serialize(hostInfo);
        var tempPath = hostInfoPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, hostInfoPath, overwrite: true);

        Log.Information("Host info written to {Path} (ipcEndpoint={Endpoint})", hostInfoPath, ipcEndpoint);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to write host-info.json - clients may not auto-discover IPC endpoint");
    }

    Log.Information("Axorith.Host started successfully (IPC: {Endpoint})", ipcEndpoint);

    // Log telemetry status after Serilog is fully configured
    Log.Information(
        "Telemetry status: enabled={Enabled}, active={Active}, isEnabled={IsEnabled}",
        telemetry!.IsEnabled,
        telemetry.IsEnabled,
        telemetry.IsEnabled);

    telemetry.TrackEvent("HostReady", new Dictionary<string, object?>
    {
        ["ipcEndpoint"] = ipcEndpoint
    });

    await app.WaitForShutdownAsync();

    telemetry?.TrackEvent("HostStopped", new Dictionary<string, object?>
    {
        ["uptimeMs"] = (long)hostUptime.Elapsed.TotalMilliseconds
    });

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Axorith.Host terminated unexpectedly");
    telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
    {
        ["fatal"] = true,
        ["message"] = TelemetryGuard.SafeString(ex.Message),
        ["stack"] = TelemetryGuard.SafeStackTrace(ex)
    });

    return 1;
}
finally
{
    try
    {
        if (File.Exists(hostInfoPath))
        {
            File.Delete(hostInfoPath);
            Log.Information("Cleaned up host-info.json on shutdown");
        }
    }
    catch
    {
        // Ignore cleanup errors
    }

    // Clean up Unix Domain Socket file on shutdown
    if (!OperatingSystem.IsWindows())
    {
        try
        {
            var socketPath = ApplicationPaths.IpcEndpoint;
            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
                Log.Information("Cleaned up IPC socket file on shutdown");
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    await Log.CloseAndFlushAsync();
    if (telemetry != null)
    {
        await telemetry.FlushAsync();
        await telemetry.DisposeAsync();
    }

    Log.Information("Host instance mutex will be released on disposal");
}

static void RegisterGlobalExceptionHandlers(ITelemetryService? telemetry)
{
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
        var exception = e.ExceptionObject as Exception;
        if (e.IsTerminating)
        {
            Log.Fatal(exception, "Unhandled exception in AppDomain (terminating)");
            telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
            {
                ["fatal"] = true,
                ["message"] = TelemetryGuard.SafeString(exception?.Message),
                ["stack"] = TelemetryGuard.SafeStackTrace(exception)
            });
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                Task.Run(() => telemetry?.FlushAsync(cts.Token), cts.Token).GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore flush errors during crash - we're terminating anyway
            }
        }
        else
        {
            Log.Error(exception, "Unhandled exception in AppDomain (non-terminating)");
            telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
            {
                ["fatal"] = false,
                ["message"] = TelemetryGuard.SafeString(exception?.Message),
                ["stack"] = TelemetryGuard.SafeStackTrace(exception)
            });
        }
    };

    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
        telemetry?.TrackEvent("ErrorOccurred", new Dictionary<string, object?>
        {
            ["fatal"] = false,
            ["message"] = TelemetryGuard.SafeString(e.Exception?.Message),
            ["stack"] = TelemetryGuard.SafeStackTrace(e.Exception)
        });
    };
}

static void RegisterCoreServices(ContainerBuilder builder)
{
    builder.Register(ctx =>
        {
            ctx.Resolve<ILoggerFactory>();
            return PlatformServices.CreateWindowService();
        })
        .As<IPlatformWindowService>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(ctx => { return PlatformServices.CreateProcessService(); })
        .As<IPlatformProcessService>()
        .SingleInstance()
        .PreserveExistingDefaults();

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

            var telemetryService = ctx.Resolve<ITelemetryService>();

            return new SessionManager(moduleRegistry, logger, validationTimeout, startupTimeout, shutdownTimeout,
                telemetryService);
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

    builder.RegisterType<HostStateService>()
        .As<IHostStateService>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.RegisterType<HostNotificationService>()
        .As<IHostNotificationService>()
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

static void EnsureIpcDirectoryExists(string ipcEndpoint)
{
    if (OperatingSystem.IsWindows())
    {
        // Named Pipes don't need directory creation
        return;
    }

    var dir = Path.GetDirectoryName(ipcEndpoint);
    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
    {
        Directory.CreateDirectory(dir);
    }
}