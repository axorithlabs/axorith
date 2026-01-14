using Axorith.Shared.Platform;

namespace Axorith.Client.Services;

/// <summary>
///     No-op implementation of IAutoStartManager for platforms where auto-start is not supported.
/// </summary>
internal sealed class NoOpAutoStartManager : IAutoStartManager
{
    public bool IsAutoStartEnabled => false;
    public bool IsStartMinimized => false;

    public bool EnableAutoStart(bool startMinimized = true)
    {
        return false;
    }

    public bool DisableAutoStart()
    {
        return true;
    }
}