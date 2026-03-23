using Axorith.Core.Services.Abstractions;

namespace Axorith.Host.Services;

/// <summary>
///     Implementation of <see cref="IHostStateService"/> that delegates to <see cref="ISessionManager"/>.
/// </summary>
public class HostStateService(ISessionManager sessionManager) : IHostStateService
{
	/// <inheritdoc />
	public bool IsSessionRunning => sessionManager.IsSessionRunning;
}
