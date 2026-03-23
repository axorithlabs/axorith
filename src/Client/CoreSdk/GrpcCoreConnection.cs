using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Client.Services.Abstractions;
using Axorith.Contracts;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using RetryPolicy = Grpc.Net.Client.Configuration.RetryPolicy;

namespace Axorith.Client.CoreSdk;

/// <summary>
///     gRPC-based implementation of ICoreConnection.
///     Manages channel lifecycle, automatic reconnection, and delegates to API implementations.
/// </summary>
public class GrpcCoreConnection : ICoreConnection
{
    private readonly string _serverAddress;
    private readonly ITokenProvider _tokenProvider;
    private readonly ILogger<GrpcCoreConnection> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly BehaviorSubject<ConnectionState> _stateSubject;
    private readonly AsyncRetryPolicy _retryPolicy;

    private GrpcChannel? _channel;
    private GrpcPresetsApi? _presetsApi;
    private GrpcSessionsApi? _sessionsApi;
    private GrpcModulesApi? _modulesApi;
    private GrpcDiagnosticsApi? _diagnosticsApi;
    private GrpcSchedulerApi? _schedulerApi;
    private GrpcNotificationApi? _notificationApi;
    private GrpcUpdatesApi? _updatesApi;
    private PresenceClient? _presenceClient;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GrpcCoreConnection" /> class.
    /// </summary>
    /// <param name="serverAddress">The gRPC server address (e.g., "http://localhost:5901").</param>
    /// <param name="tokenProvider">The provider for retrieving the authentication token.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="loggerFactory">The logger factory for creating loggers.</param>
    public GrpcCoreConnection(
        string serverAddress,
        ITokenProvider tokenProvider,
        ILogger<GrpcCoreConnection> logger,
        ILoggerFactory loggerFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        _serverAddress = serverAddress;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _stateSubject = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);

        _retryPolicy = Policy
            .Handle<RpcException>(ex =>
                ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal)
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, _) =>
                {
                    _logger.LogWarning(exception,
                        "gRPC call failed, retry {RetryCount} after {Delay}s",
                        retryCount, timeSpan.TotalSeconds);
                });
    }

    /// <inheritdoc />
    public IPresetsApi Presets =>
        _presetsApi ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

    /// <inheritdoc />
    public ISessionsApi Sessions => _sessionsApi
                                    ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

    /// <inheritdoc />
    public IModulesApi Modules => _modulesApi
                                  ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

    /// <inheritdoc />
    public IDiagnosticsApi Diagnostics => _diagnosticsApi
                                          ?? throw new InvalidOperationException(
                                              "Not connected. Call ConnectAsync first.");

    /// <inheritdoc />
    public ISchedulerApi Scheduler => _schedulerApi
                                      ?? throw new InvalidOperationException(
                                          "Not connected. Call ConnectAsync first.");

    /// <inheritdoc />
    public INotificationApi Notifications => _notificationApi
                                             ?? throw new InvalidOperationException(
                                                 "Not connected. Call ConnectAsync first.");

    /// <inheritdoc />
    public IUpdatesApi Updates => _updatesApi
                                  ?? throw new InvalidOperationException(
                                      "Not connected. Call ConnectAsync first.");

    /// <inheritdoc />
    public ConnectionState State => _stateSubject.Value;

    /// <inheritdoc />
    public IObservable<ConnectionState> StateChanged => _stateSubject.AsObservable();

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(GrpcCoreConnection));

        if (State == ConnectionState.Connected)
        {
            _logger.LogWarning("Already connected to {Address}", _serverAddress);
            return;
        }

        SetState(ConnectionState.Connecting);

        try
        {
            var token = await _tokenProvider.GetTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException(
                    "Failed to retrieve authentication token. Ensure Axorith.Host is running.");
            }

            _logger.LogInformation("Connecting to Axorith.Host at {Address}", _serverAddress);

            var clientVersion = VersionHelper.GetClientVersion();

            var credentials = CallCredentials.FromInterceptor((_, metadata) =>
            {
                metadata.Add(AuthConstants.VersionHeaderName, clientVersion);
                metadata.Add(AuthConstants.TokenHeaderName, token);
                return Task.CompletedTask;
            });

            ChannelCredentials channelCredentials;
            GrpcChannelOptions channelOptions;

            if (_serverAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                channelCredentials = ChannelCredentials.Create(ChannelCredentials.SecureSsl, credentials);
                channelOptions = new GrpcChannelOptions
                {
                    MaxReceiveMessageSize = 16 * 1024 * 1024,
                    MaxSendMessageSize = 16 * 1024 * 1024,
                    Credentials = channelCredentials,
                    ServiceConfig = new ServiceConfig
                    {
                        MethodConfigs =
                        {
                            new MethodConfig
                            {
                                Names = { MethodName.Default },
                                RetryPolicy = new RetryPolicy
                                {
                                    MaxAttempts = 5,
                                    InitialBackoff = TimeSpan.FromSeconds(1),
                                    MaxBackoff = TimeSpan.FromSeconds(5),
                                    BackoffMultiplier = 1.5,
                                    RetryableStatusCodes =
                                        { StatusCode.Unavailable, StatusCode.DeadlineExceeded, StatusCode.Internal }
                                }
                            }
                        }
                    }
                };
            }
            else if (_serverAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Using insecure channel for localhost communication. " +
                    "This is acceptable only for local IPC. Never use in production over network.");

                channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials);
                channelOptions = new GrpcChannelOptions
                {
                    MaxReceiveMessageSize = 16 * 1024 * 1024,
                    MaxSendMessageSize = 16 * 1024 * 1024,
                    Credentials = channelCredentials,
                    UnsafeUseInsecureChannelCallCredentials = true,
                    ServiceConfig = new ServiceConfig
                    {
                        MethodConfigs =
                        {
                            new MethodConfig
                            {
                                Names = { MethodName.Default },
                                RetryPolicy = new RetryPolicy
                                {
                                    MaxAttempts = 5,
                                    InitialBackoff = TimeSpan.FromSeconds(1),
                                    MaxBackoff = TimeSpan.FromSeconds(5),
                                    BackoffMultiplier = 1.5,
                                    RetryableStatusCodes =
                                        { StatusCode.Unavailable, StatusCode.DeadlineExceeded, StatusCode.Internal }
                                }
                            }
                        }
                    }
                };
            }
            else
            {
                _logger.LogInformation("Using IPC endpoint: {Endpoint}", _serverAddress);

                channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials);
                channelOptions = new GrpcChannelOptions
                {
                    MaxReceiveMessageSize = 16 * 1024 * 1024,
                    MaxSendMessageSize = 16 * 1024 * 1024,
                    Credentials = channelCredentials,
                    UnsafeUseInsecureChannelCallCredentials = true,
                    HttpHandler = new SocketsHttpHandler
                    {
                        ConnectCallback = CreateIpcConnectCallback(_serverAddress),
                    },
                    ServiceConfig = new ServiceConfig
                    {
                        MethodConfigs =
                        {
                            new MethodConfig
                            {
                                Names = { MethodName.Default },
                                RetryPolicy = new RetryPolicy
                                {
                                    MaxAttempts = 5,
                                    InitialBackoff = TimeSpan.FromSeconds(1),
                                    MaxBackoff = TimeSpan.FromSeconds(5),
                                    BackoffMultiplier = 1.5,
                                    RetryableStatusCodes =
                                        { StatusCode.Unavailable, StatusCode.DeadlineExceeded, StatusCode.Internal }
                                }
                            }
                        }
                    }
                };
            }

            _channel = GrpcChannel.ForAddress(
                _serverAddress.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? _serverAddress
                    : "http://localhost",
                channelOptions);

            var presetsClient = new PresetsService.PresetsServiceClient(_channel);
            var sessionsClient = new SessionsService.SessionsServiceClient(_channel);
            var modulesClient = new ModulesService.ModulesServiceClient(_channel);
            var diagnosticsClient = new DiagnosticsService.DiagnosticsServiceClient(_channel);
            var schedulerClient = new SchedulerService.SchedulerServiceClient(_channel);
            var notificationClient = new NotificationService.NotificationServiceClient(_channel);
            var updatesClient = new UpdatesService.UpdatesServiceClient(_channel);

            _presetsApi = new GrpcPresetsApi(presetsClient, _retryPolicy);
            _sessionsApi = new GrpcSessionsApi(sessionsClient, _retryPolicy, _logger);
            _modulesApi = new GrpcModulesApi(modulesClient, _retryPolicy, _logger);
            _diagnosticsApi = new GrpcDiagnosticsApi(diagnosticsClient, _retryPolicy);
            _schedulerApi = new GrpcSchedulerApi(schedulerClient, _retryPolicy);
            _notificationApi = new GrpcNotificationApi(notificationClient);
            _updatesApi = new GrpcUpdatesApi(updatesClient, _loggerFactory.CreateLogger<GrpcUpdatesApi>());

            // Create and start presence streaming
            var presenceServiceClient = new Contracts.Generated.PresenceService.PresenceServiceClient(_channel);
            _presenceClient = new PresenceClient(
                presenceServiceClient,
                _loggerFactory.CreateLogger<PresenceClient>());
            await _presenceClient.StartPresenceStreamAsync(ct).ConfigureAwait(false);

            var health = await _diagnosticsApi.GetHealthAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("Connected successfully to Axorith.Host v{Version} ({State})",
                health.Version, health.State);

            SetState(ConnectionState.Connected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to {Address}", _serverAddress);
            SetState(ConnectionState.Failed);

            await DisposeChannelAsync().ConfigureAwait(false);

            throw new InvalidOperationException($"Failed to connect to Axorith.Host at {_serverAddress}", ex);
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        if (State == ConnectionState.Disconnected)
        {
            return;
        }

        _logger.LogInformation("Disconnecting from {Address}", _serverAddress);

        // Stop presence stream gracefully before disconnecting
        if (_presenceClient != null)
        {
            await _presenceClient.StopPresenceStreamAsync().ConfigureAwait(false);
        }

        SetState(ConnectionState.Disconnected);

        await DisposeChannelAsync().ConfigureAwait(false);

        _logger.LogInformation("Disconnected successfully");
    }

    private async Task DisposeChannelAsync()
    {
        if (_presenceClient != null)
        {
            await _presenceClient.DisposeAsync().ConfigureAwait(false);
            _presenceClient = null;
        }

        if (_sessionsApi is IDisposable sessionsDisposable)
        {
            sessionsDisposable.Dispose();
        }

        if (_modulesApi is IDisposable modulesDisposable)
        {
            modulesDisposable.Dispose();
        }

        _presetsApi = null;
        _sessionsApi = null;
        _modulesApi = null;
        _diagnosticsApi = null;
        _schedulerApi = null;
        _notificationApi = null;

        if (_channel != null)
        {
            try
            {
                await _channel.ShutdownAsync().ConfigureAwait(false);
                _channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disposing gRPC channel");
            }
            finally
            {
                _channel = null;
            }
        }
    }

    private void SetState(ConnectionState newState)
    {
        if (_stateSubject.Value == newState)
        {
            return;
        }

        _logger.LogDebug("Connection state changed: {OldState} -> {NewState}",
            _stateSubject.Value, newState);
        _stateSubject.OnNext(newState);
    }

    /// <summary>
    ///     Creates a ConnectCallback for IPC transport (Unix Domain Socket or Named Pipe).
    /// </summary>
    private static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateIpcConnectCallback(
        string ipcEndpoint)
    {
        if (OperatingSystem.IsWindows())
        {
            // Named Pipe
            return async (_, ct) =>
            {
                var pipe = new System.IO.Pipes.NamedPipeClientStream(
                    ".", ipcEndpoint, System.IO.Pipes.PipeDirection.InOut,
                    System.IO.Pipes.PipeOptions.Asynchronous);
                await pipe.ConnectAsync(5000, ct);
                return pipe;
            };
        }

        // Unix Domain Socket
        return async (_, ct) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(ipcEndpoint), ct);
            return new NetworkStream(socket, ownsSocket: true);
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await DisconnectAsync().ConfigureAwait(false);

        _stateSubject.Dispose();

        GC.SuppressFinalize(this);
    }
}