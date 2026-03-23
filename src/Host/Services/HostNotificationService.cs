using Axorith.Shared.Platform;

namespace Axorith.Host.Services;

/// <summary>
///     Implementation of <see cref="IHostNotificationService"/> that sends OS notifications on Client crash.
/// </summary>
public class HostNotificationService(
	ISystemNotificationService systemNotificationService,
	IHostStateService hostStateService,
	ILogger<HostNotificationService> logger) : IHostNotificationService
{
	/// <inheritdoc />
	public async Task NotifyClientCrashAsync(string clientVersion, CancellationToken ct = default)
	{
		try
		{
			var hasSession = hostStateService.IsSessionRunning;
			var title = hasSession
				? "Axorith interface closed unexpectedly"
				: "Axorith interface disconnected";
			var body = hasSession
				? $"Active sessions continue running. Restart the app to manage. (Client v{clientVersion})"
				: $"Client disconnected unexpectedly. Restart if needed. (Client v{clientVersion})";

			logger.LogWarning(
				"Client disconnected unexpectedly (version {ClientVersion}). {SessionStatus}",
				clientVersion,
				hasSession ? "Active sessions continue running." : "No active sessions.");

			await systemNotificationService.ShowNotificationAsync(
				title,
				body,
				TimeSpan.FromMinutes(5)).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to send crash notification for client version {ClientVersion}", clientVersion);
		}
	}
}
