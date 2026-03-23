using Axorith.Contracts.Generated;
using Grpc.Core;

namespace Axorith.Host.Services;

/// <summary>
///     gRPC service implementation for client presence streaming.
///     Detects client crashes (unexpected disconnect) vs graceful disconnects.
///
///     Graceful exit detection: before closing the presence stream, the client calls
///     HostManagement.NotifyClientExiting (unary RPC) which sets a static flag.
///     When the presence stream closes, the flag is checked to distinguish exit from crash.
/// </summary>
public class PresenceServiceImpl(
	IHostNotificationService hostNotificationService,
	ILogger<PresenceServiceImpl> logger) : PresenceService.PresenceServiceBase
{
	private static int _clientIsExiting;

	/// <summary>
	///     Called by HostManagementServiceImpl when the client signals it's about to exit.
	/// </summary>
	public static void MarkClientExiting() => Interlocked.Exchange(ref _clientIsExiting, 1);

	/// <summary>
	///     Resets the flag. Call this when a new client connects.
	/// </summary>
	private static bool ConsumeExitingFlag() => Interlocked.Exchange(ref _clientIsExiting, 0) == 1;

	/// <inheritdoc />
	public override async Task StreamClientPresence(
		IAsyncStreamReader<PresenceMessage> requestStream,
		IServerStreamWriter<PresenceAck> responseStream,
		ServerCallContext context)
	{
		// New connection — reset any stale flag from a previous session
		ConsumeExitingFlag();

		var isExpectedDisconnect = false;
		string? clientVersion = null;

		try
		{
			if (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
			{
				var initialMessage = requestStream.Current;
				clientVersion = initialMessage.ClientVersion;

				if (initialMessage.IsDisconnect)
				{
					isExpectedDisconnect = true;
				}

				logger.LogInformation(
					"Client connected (version {ClientVersion})",
					clientVersion);

				await responseStream.WriteAsync(new PresenceAck
				{
					ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
					Acknowledged = true
				}, context.CancellationToken).ConfigureAwait(false);
			}

			while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
			{
				var message = requestStream.Current;

				if (message.IsDisconnect)
				{
					isExpectedDisconnect = true;
					logger.LogInformation(
						"Client signaled graceful disconnect (version {ClientVersion})",
						clientVersion ?? "unknown");
				}
			}
		}
		catch (OperationCanceledException)
		{
			logger.LogDebug("Presence stream cancelled");
		}
		catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
		{
			logger.LogDebug("Presence stream cancelled by client");
		}
		finally
		{
			if (!isExpectedDisconnect && ConsumeExitingFlag())
			{
				isExpectedDisconnect = true;
				logger.LogInformation(
					"Client exit signaled via RPC (version {ClientVersion})",
					clientVersion ?? "unknown");
			}

			if (!isExpectedDisconnect)
			{
				logger.LogWarning(
					"Client disconnected unexpectedly (version {ClientVersion})",
					clientVersion ?? "unknown");

				await hostNotificationService.NotifyClientCrashAsync(
					clientVersion ?? "unknown",
					CancellationToken.None).ConfigureAwait(false);
			}
			else
			{
				logger.LogInformation(
					"Client disconnected gracefully (version {ClientVersion})",
					clientVersion ?? "unknown");
			}
		}
	}
}
