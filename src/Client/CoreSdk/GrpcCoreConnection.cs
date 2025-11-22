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
    private readonly BehaviorSubject<ConnectionState> _stateSubject;
    private readonly AsyncRetryPolicy _retryPolicy;

    private GrpcChannel? _channel;
    private GrpcPresetsApi? _presetsApi;
    private GrpcSessionsApi? _sessionsApi;
    private GrpcModulesApi? _modulesApi;
    private GrpcDiagnosticsApi? _diagnosticsApi;
    private GrpcSchedulerApi? _schedulerApi;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GrpcCoreConnection" /> class.
    /// </summary>
    /// <param name="serverAddress">The gRPC server address (e.g., "http://localhost:5901").</param>
    /// <param name="tokenProvider">The provider for retrieving the authentication token.</param>
    /// <param name="logger">The logger instance.</param>
    public GrpcCoreConnection(
        string serverAddress,
        ITokenProvider tokenProvider,
        ILogger<GrpcCoreConnection> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverAddress);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        _serverAddress = serverAddress;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _stateSubject = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);

        _retryPolicy = Policy
            .Handle<RpcException>(ex => ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            .WaitAndRetryAsync(
                retryCount: 3,
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

            var credentials = CallCredentials.FromInterceptor((_, metadata) =>
            {
                metadata.Add(AuthConstants.TokenHeaderName, token);
                return Task.CompletedTask;
            });

            var channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials);

            _channel = GrpcChannel.ForAddress(_serverAddress, new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 16 * 1024 * 1024, // 16MB
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
                                RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.DeadlineExceeded }
                            }
                        }
                    }
                }
            });

            var presetsClient = new PresetsService.PresetsServiceClient(_channel);
            var sessionsClient = new SessionsService.SessionsServiceClient(_channel);
            var modulesClient = new ModulesService.ModulesServiceClient(_channel);
            var diagnosticsClient = new DiagnosticsService.DiagnosticsServiceClient(_channel);
            var schedulerClient = new SchedulerService.SchedulerServiceClient(_channel);

            _presetsApi = new GrpcPresetsApi(presetsClient, _retryPolicy);
            _sessionsApi = new GrpcSessionsApi(sessionsClient, _retryPolicy, _logger);
            _modulesApi = new GrpcModulesApi(modulesClient, _retryPolicy, _logger);
            _diagnosticsApi = new GrpcDiagnosticsApi(diagnosticsClient, _retryPolicy);
            _schedulerApi = new GrpcSchedulerApi(schedulerClient, _retryPolicy);

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

        SetState(ConnectionState.Disconnected);

        await DisposeChannelAsync().ConfigureAwait(false);

        _logger.LogInformation("Disconnected successfully");
    }

    private async Task DisposeChannelAsync()
    {
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