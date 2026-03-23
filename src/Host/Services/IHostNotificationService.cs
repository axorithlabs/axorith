namespace Axorith.Host.Services;

/// <summary>
///     Provides crash notification capabilities for the Host process.
///     Sends OS-level notifications when the Client disconnects unexpectedly.
/// </summary>
public interface IHostNotificationService
{
	/// <summary>
	///     Notifies the user that the Client has crashed unexpectedly.
	/// </summary>
	/// <param name="clientVersion">The version of the client that crashed.</param>
	/// <param name="ct">Cancellation token.</param>
	Task NotifyClientCrashAsync(string clientVersion, CancellationToken ct = default);
}
