using Axorith.Shared.Platform;

namespace Axorith.Shared.Platform;

/// <summary>
///     Defines a service for blocking applications from running.
///     Operates in BlockList mode only: explicitly listed processes are terminated.
/// </summary>
public interface IProcessBlocker : IDisposable
{
    /// <summary>
    ///     Applies blocking rules for the specified list of processes.
    ///     This starts the monitoring if not already running, or updates the existing rules.
    ///     Existing processes matching the list will be terminated immediately.
    /// </summary>
    /// <param name="processNames">
    ///     The list of process names to block (e.g. "discord", "steam").
    /// </param>
    void Block(IEnumerable<string> processNames);

    /// <summary>
    ///     Dynamically removes a restriction for a specific process without stopping the whole blocker.
    /// </summary>
    /// <param name="processName">The process name to unblock.</param>
    void Unblock(string processName);

    /// <summary>
    ///     Stops all blocking activity, clears the list, and releases monitoring resources.
    /// </summary>
    void UnblockAll();
}