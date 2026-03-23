using Axorith.Contracts;
using Axorith.Contracts.Generated;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.CoreSdk;

/// <summary>
///     Manages the presence streaming connection between Client and Host.
///     Sends periodic heartbeats and a disconnect message on graceful shutdown.
///     The Host uses stream termination without disconnect to detect crashes.
/// </summary>
public class PresenceClient : IAsyncDisposable
{
	private readonly PresenceService.PresenceServiceClient _client;
	private readonly ILogger<PresenceClient> _logger;

	private AsyncDuplexStreamingCall<PresenceMessage, PresenceAck>? _call;
	private Task? _readLoopTask;
	private CancellationTokenSource? _cts;
	private bool _disposed;

	/// <summary>
	///     Initializes a new instance of the <see cref="PresenceClient"/> class.
	/// </summary>
	/// <param name="client">The gRPC presence service client.</param>
	/// <param name="logger">The logger instance.</param>
	public PresenceClient(
		PresenceService.PresenceServiceClient client,
		ILogger<PresenceClient> logger)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
		_logger = logger;
	}

	/// <summary>
	///     Starts the presence stream by sending an initial presence message
	///     and keeping the stream open for the lifetime of the client.
	/// </summary>
	/// <param name="ct">Cancellation token to stop the presence stream.</param>
	public async Task StartPresenceStreamAsync(CancellationToken ct = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, nameof(PresenceClient));

		if (_call != null)
		{
			_logger.LogWarning("Presence stream already started");
			return;
		}

		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		var token = _cts.Token;

		try
		{
			_call = _client.StreamClientPresence(cancellationToken: token);

			var clientVersion = VersionHelper.GetClientVersion();

			// Send initial presence message
			await _call.RequestStream.WriteAsync(new PresenceMessage
			{
				Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ClientVersion = clientVersion,
				IsDisconnect = false
			}, token).ConfigureAwait(false);

			_logger.LogInformation("Presence stream started (version {ClientVersion})", clientVersion);

			// Start reading acknowledgments in background
			_readLoopTask = Task.Run(async () =>
			{
				try
				{
					await foreach (var ack in _call.ResponseStream.ReadAllAsync(token).ConfigureAwait(false))
					{
						_logger.LogDebug(
							"Presence ack received: server_timestamp={ServerTimestamp}",
							ack.ServerTimestamp);
					}
				}
				catch (OperationCanceledException)
				{
					_logger.LogDebug("Presence read loop cancelled");
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Presence read loop ended with error");
				}
			}, token);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to start presence stream");
			await DisposeCallAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <summary>
	///     Stops the presence stream gracefully by sending a disconnect message
	///     and waiting for server acknowledgment before closing the stream.
	///     This tells the Host this was an intentional shutdown, not a crash.
	/// </summary>
	public async Task StopPresenceStreamAsync()
	{
		if (_call == null)
		{
			return;
		}

		try
		{
			// Send disconnect message to signal graceful shutdown
			await _call.RequestStream.WriteAsync(new PresenceMessage
			{
				Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				ClientVersion = VersionHelper.GetClientVersion(),
				IsDisconnect = true
			}).ConfigureAwait(false);

			_logger.LogInformation("Sent presence disconnect message");

			// Wait for server to ack the disconnect before closing the stream.
			// This prevents the race where END_STREAM arrives before the message is processed.
			try
			{
				await foreach (var ack in _call.ResponseStream.ReadAllAsync().ConfigureAwait(false))
				{
					_logger.LogDebug("Disconnect ack received");
					break;
				}
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Did not receive disconnect ack (server may have already processed it)");
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to send presence disconnect message");
		}

		try
		{
			// Complete the request stream
			await _call.RequestStream.CompleteAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to complete presence request stream");
		}

		// Wait briefly for the read loop to finish
		if (_readLoopTask != null)
		{
			try
			{
				_cts?.Cancel();
				await _readLoopTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
			}
			catch (Exception)
			{
				// Expected if stream was already cancelled
			}
		}

		await DisposeCallAsync().ConfigureAwait(false);
		_logger.LogInformation("Presence stream stopped gracefully");
	}

	private async Task DisposeCallAsync()
	{
		if (_call != null)
		{
			try
			{
				_call.Dispose();
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Error disposing presence call");
			}
			_call = null;
		}

		if (_cts != null)
		{
			_cts.Dispose();
			_cts = null;
		}

		_readLoopTask = null;

		await Task.CompletedTask;
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		await StopPresenceStreamAsync().ConfigureAwait(false);

		GC.SuppressFinalize(this);
	}
}
