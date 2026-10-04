using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Axorith.Core.Logging;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Core.Models;
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
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

if (args.Contains("--check-committed-session", StringComparer.OrdinalIgnoreCase))
{
    try
    {
        var checkArgs = args.Where(arg => !string.Equals(arg, "--check-committed-session",
            StringComparison.OrdinalIgnoreCase)).ToArray();
        var checkBuilder = WebApplication.CreateBuilder(checkArgs);
        var checkConfiguration = new Configuration();
        checkBuilder.Configuration.Bind(checkConfiguration);
        var configPath = checkConfiguration.Persistence.ResolveConfigPath();
        var secureState = !checkBuilder.Environment.IsDevelopment();
        var commitmentStateDirectory = CommitmentStatePaths.Resolve(configPath, secureState, migrateLegacy: false);
        var committedSessionPath = Path.Combine(commitmentStateDirectory, "committed-session.json");
        if (!File.Exists(committedSessionPath) && secureState)
        {
            committedSessionPath = Path.Combine(configPath, "committed-session.json");
        }

        if (File.Exists(committedSessionPath))
        {
            using var document = JsonDocument.Parse(CommittedSessionStateFile.ReadPayloadOrLegacy(committedSessionPath));
            var root = document.RootElement;
            var mode = root.GetProperty("Preset").GetProperty("FocusCommitment").GetProperty("Mode").GetInt32();
            if (mode is 1 or 2)
            {
                if (!DateTimeOffset.TryParse(root.GetProperty("EndDeadline").GetString(), out var deadline) ||
                    deadline > DateTimeOffset.UtcNow)
                {
                    if (deadline != default)
                    {
                        Console.WriteLine($"A committed session is active until {deadline.ToLocalTime():yyyy-MM-dd HH:mm}.");
                    }
                    else
                    {
                        Console.WriteLine("A committed session is active, but its end time could not be verified.");
                    }

                    return 2;
                }
            }

            if (mode is not (0 or 1 or 2))
            {
                return 2;
            }

            if (mode == 2)
            {
                var protection = new WindowsCommitmentProtectionService(Path.GetDirectoryName(committedSessionPath)!,
                    NullLogger<WindowsCommitmentProtectionService>.Instance, allowLegacyState: true);
                await protection.CheckRecoveryStateAsync().ConfigureAwait(false);
                await protection.RestoreAsync().ConfigureAwait(false);
            }

            File.Delete(committedSessionPath);
            var tempPath = committedSessionPath + ".tmp";
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        var strictStateDirectory = File.Exists(Path.Combine(commitmentStateDirectory, "strict-protection.json"))
            ? commitmentStateDirectory
            : configPath;
        if (File.Exists(Path.Combine(strictStateDirectory, "strict-protection.json")))
        {
            var protection = new WindowsCommitmentProtectionService(strictStateDirectory,
                NullLogger<WindowsCommitmentProtectionService>.Instance, allowLegacyState: true);
            await protection.RestoreAsync().ConfigureAwait(false);
        }

        await new WindowsCommitmentProtectionService(commitmentStateDirectory,
                NullLogger<WindowsCommitmentProtectionService>.Instance)
            .SetRecoveryStartupAsync(active: false).ConfigureAwait(false);

        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Axorith could not verify the committed session: {ex.Message}");
        return 2;
    }
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var hostInfoPath = ApplicationPaths.HostInfoFile;
ITelemetryService? telemetry = null;
FileSystemWatcher? telemetryPreferenceWatcher = null;

using var hostMutex = new Mutex(true, "Global\\AxorithHostInstanceMutex", out var createdNew);

if (!createdNew)
{
    Log.Warning("Another Axorith.Host instance is already running. Exiting.");

    await Task.Delay(1000);

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

var legacyHostInfoPath = Path.Combine(ApplicationPaths.RoamingRoot, "host-info.json");
if (!string.Equals(hostInfoPath, legacyHostInfoPath, StringComparison.OrdinalIgnoreCase))
{
    try
    {
        File.Delete(legacyHostInfoPath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        // The host writes runtime connection information to the machine-wide path now.
    }
}

try
{
    Log.Information("Starting Axorith.Host...");

    var builder = WebApplication.CreateBuilder(args);
    var isTestRun = Environment.GetEnvironmentVariable("AXORITH_TEST_MODE") == "1";
    var telemetryEnabled = !isTestRun && TelemetryPreference.ReadOrDefault(true);
    var telemetrySettings = new TelemetrySettings()
            .WithEnvironmentOverrides() with
        {
            ApplicationName = "Axorith.Host",
            Enabled = telemetryEnabled
        };

    telemetry = isTestRun ? NoopTelemetryService.Instance : new TelemetryService(telemetrySettings);
    try
    {
        if (!isTestRun)
            telemetryPreferenceWatcher = TelemetryPreference.Watch(enabled => telemetry?.SetEnabled(enabled));
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Could not watch the shared telemetry preference file");
    }
    TelemetryRuntime.RegisterGlobalExceptionHandlers(telemetry, "host");

    TelemetryRuntime.LogConfiguration("Host", telemetrySettings, telemetry?.IsEnabled == true);
    if (!telemetrySettings.IsActive)
        Log.Information("To enable telemetry, set AXORITH_TELEMETRY_API_KEY environment variable");
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
            .WriteTo.File(
                Path.Combine(resolvedLogsPath, "host-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {ShortSourceContext}: {ModuleContext}{Message:lj}{NewLine}{Exception}");
    });

    builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

    builder.Services.AddSingleton(_ => telemetry ?? NoopTelemetryService.Instance);
    builder.Services.AddSingleton<UserRegistrationService>();
    builder.Services.AddSingleton<UpdateService>();
    builder.Services.Configure<Configuration>(builder.Configuration);

    var config = builder.Configuration.Get<Configuration>() ?? new Configuration();
    hostInfoPath = config.Persistence.ResolveHostInfoPath();
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

    builder.Services.AddSingleton(sp =>
        PlatformServices.CreateFilePermissionsService(sp.GetRequiredService<ILoggerFactory>()));
    builder.Services.AddSingleton<HostAuthenticationService>();

    if (!isTestRun)
        builder.Services.AddHostedService<NativeMessagingRegistrar>();

    builder.Services.AddGrpc(options =>
    {
        options.MaxReceiveMessageSize = 16 * 1024 * 1024;
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();

        options.Interceptors.Add<AuthenticationInterceptor>();
        options.Interceptors.Add<GrpcExceptionInterceptor>();
    });

    builder.Services.AddHttpClient("default");


    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddGrpcReflection();
    }

    builder.Host.ConfigureContainer<ContainerBuilder>((_, containerBuilder) =>
    {
        RegisterCoreServices(containerBuilder, !builder.Environment.IsDevelopment());
        RegisterBroadcasters(containerBuilder);
    });

    var app = builder.Build();
    hostInfoPath = app.Services.GetRequiredService<IOptions<Configuration>>()
        .Value.Persistence.ResolveHostInfoPath();

    try
    {
        var authService = app.Services.GetRequiredService<HostAuthenticationService>();
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
            var registrationService = app.Services.GetRequiredService<UserRegistrationService>();
            var registration = await registrationService.GetOrCreateAsync(app.Lifetime.ApplicationStopping)
                .ConfigureAwait(false);

            Log.Information("User registration initialized: MachineId={MachineId}, FirstSeen={FirstSeen}",
                registration.MachineId[..8] + "...",
                registration.FirstSeenUtc);

        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize user registration");
        }
    }, app.Lifetime.ApplicationStopping);

    var moduleRegistryInitTask = Task.Run(async () =>
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
        }
        catch (Exception initEx)
        {
            Log.Warning(initEx, "ModuleRegistry initialization failed; continuing without modules");
        }
    }, app.Lifetime.ApplicationStopping);

    var schedulerStartTask = Task.Run(async () =>
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

    var autoStopStartTask = Task.Run(async () =>
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

    await Task.WhenAll(moduleRegistryInitTask, schedulerStartTask, autoStopStartTask).ConfigureAwait(false);
    try
    {
        var sessionManager = app.Services.GetRequiredService<ISessionManager>();
        await sessionManager.RecoverCommittedSessionAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        var recoveredSession = app.Services.GetRequiredService<ISessionManager>();
        var commitmentProtection = app.Services.GetRequiredService<ICommitmentProtectionService>();
        var strictSessionActive = recoveredSession.ActiveSession?.FocusCommitment.IsStrict == true;
        await commitmentProtection.ReconcileAsync(strictSessionActive,
            app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        await commitmentProtection.SetRecoveryStartupAsync(
            recoveredSession.ActiveSession?.FocusCommitment.IsCommitted == true,
            app.Lifetime.ApplicationStopping).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to recover the committed session. Host cannot continue safely.");
        Log.Fatal("Existing committed recovery and Windows protection state were preserved for a later retry.");
        return 1;
    }

    await app.Services.GetRequiredService<IScheduleManager>()
        .StartProcessingAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);

    app.MapGrpcService<PresetsServiceImpl>();
    app.MapGrpcService<SessionsServiceImpl>();
    app.MapGrpcService<ModulesServiceImpl>();
    app.MapGrpcService<DiagnosticsServiceImpl>();
    app.MapGrpcService<HostManagementServiceImpl>();
    app.MapGrpcService<SchedulerServiceImpl>();
    app.MapGrpcService<NotificationServiceImpl>();
    app.MapGrpcService<GrpcUpdatesService>();

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

            var hostInfo = new
                { port = boundPort, address = config.Grpc.BindAddress, timestamp = DateTimeOffset.UtcNow };

            // Use FileShare.Read to allow clients to read while we're writing
            // This prevents "file is being used by another process" errors
            var json = JsonSerializer.Serialize(hostInfo);
            await using (var stream = new FileStream(
                             hostInfoPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.Read, // Allow concurrent reads
                             bufferSize: 4096,
                             useAsync: true))
            {
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(json);
                await writer.FlushAsync();
            }

            Log.Information("Host info written to {Path} (port={Port})", hostInfoPath, boundPort);
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

    Log.Information(
        "Telemetry status: enabled={Enabled}, active={Active}, isEnabled={IsEnabled}",
        telemetrySettings.Enabled,
        telemetrySettings.IsActive,
        telemetry!.IsEnabled);

    await app.WaitForShutdownAsync();

    telemetry?.TrackEvent("HostStopped");

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Axorith.Host terminated unexpectedly");
    telemetry?.TrackError(ex, "host", "startup", "fatal", handled: true, fatal: true);

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

    await Log.CloseAndFlushAsync();
    telemetryPreferenceWatcher?.Dispose();
    if (telemetry != null)
    {
        using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await telemetry.FlushAsync(flushCts.Token);
    }

    Log.Information("Host instance mutex will be released on disposal");
}

static void RegisterCoreServices(ContainerBuilder builder, bool secureCommitmentState)
{
    builder.Register(_ => PlatformServices.CreateWindowService())
        .As<IPlatformWindowService>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(_ => PlatformServices.CreateProcessService())
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
            var secureStorage = ctx.Resolve<ISecureStorageService>();
            var commitmentStateDirectory = CommitmentStatePaths.Resolve(config.Persistence.ResolveConfigPath(),
                secureCommitmentState);
            var committedSessionPath = Path.Combine(commitmentStateDirectory, "committed-session.json");
            var commitmentProtection = ctx.Resolve<ICommitmentProtectionService>();

            return new SessionManager(moduleRegistry, logger, validationTimeout, startupTimeout, shutdownTimeout,
                telemetryService, committedSessionPath, commitmentProtection,
                Path.Combine(config.Persistence.ResolveConfigPath(), "session-history.json"), secureStorage);
        })
        .As<ISessionManager>()
        .SingleInstance()
        .PreserveExistingDefaults();

    builder.Register(ctx =>
        {
            var config = ctx.Resolve<IOptions<Configuration>>().Value;
            var logger = ctx.Resolve<ILogger<WindowsCommitmentProtectionService>>();
            return new WindowsCommitmentProtectionService(
                CommitmentStatePaths.Resolve(config.Persistence.ResolveConfigPath(), secureCommitmentState), logger);
        })
        .As<ICommitmentProtectionService>()
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
            var telemetryService = ctx.Resolve<ITelemetryService>();

            return new ScheduleManager(rootDataDir, sessionManager, presetManager, autoStopService, notifier, logger,
                telemetryService);
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
            var telemetryService = ctx.Resolve<ITelemetryService>();

            return new SessionAutoStopService(sessionManager, presetManager, notifier, logger, telemetryService);
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
