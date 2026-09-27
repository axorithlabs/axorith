namespace Axorith.Shared.Platform;

/// <summary>
///     Defines a service for blocking applications from running.
///     Terminates explicitly listed processes or all non-allowed user-session processes.
/// </summary>
public interface IProcessBlocker : IDisposable
{
    /// <summary>Whether a process monitoring loop is currently active.</summary>
    bool IsMonitoring { get; }

    /// <summary>
    ///     Applies blocking rules for the specified list of processes.
    ///     This starts the monitoring if not already running, or updates the existing rules.
    ///     Existing processes matching the list will be terminated immediately.
    /// </summary>
    /// <param name="processNames">
    ///     The list of process names to block (e.g. "discord", "steam").
    /// </param>
    /// <returns>
    ///     A list of unique process names that were terminated during the initial scan.
    ///     Useful for notifying the user about what was closed.
    /// </returns>
    List<string> Block(IEnumerable<string> processNames);

    /// <summary>
    ///     Allows the specified applications and their child processes while terminating other
    ///     applications in the current interactive Windows session.
    /// </summary>
    List<string> AllowOnly(IEnumerable<string> processNames);

    /// <summary>
    ///     Dynamically removes a restriction for a specific process without stopping the whole blocker.
    /// </summary>
    /// <param name="processName">The process name to unblock.</param>
    void Unblock(string processName);

    /// <summary>
    ///     Stops all blocking activity, clears the list, and releases monitoring resources.
    /// </summary>
    void UnblockAll();

    /// <summary>
    ///     Fires when a process is successfully blocked (terminated) during runtime monitoring.
    ///     Argument is the name of the blocked process.
    /// </summary>
    event Action<string>? ProcessBlocked;
}
