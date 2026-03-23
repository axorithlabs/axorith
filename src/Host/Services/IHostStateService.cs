namespace Axorith.Host.Services;

/// <summary>
///     Provides session state information for Host-side services.
///     Abstracts session status checking for use by PresenceService and other services.
/// </summary>
public interface IHostStateService
{
	/// <summary>
	///     Gets a value indicating whether a session is currently running.
	/// </summary>
	bool IsSessionRunning { get; }
}
