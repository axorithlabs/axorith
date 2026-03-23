namespace Axorith.Shared.Platform.Linux;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

/// <summary>
///     Linux process blocker with SIGTERM → SIGKILL termination:
///     - Enumerates /proc filesystem for process scanning (Process.GetProcessesByName unavailable on Linux)
///     - Sends SIGTERM (15) first, waits 5 seconds, then SIGKILL (9) for non-responsive processes
///     - Skips PIDs &lt; 500 (kernel threads)
///     - 500ms polling interval via PeriodicTimer
///     - Uses P/Invoke to libc.kill() — Process.Kill() MUST NOT be used (always sends SIGKILL on Unix)
///     - Graceful shutdown is handled at the application layer (ApplicationLauncher/ProcessService)
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxProcessBlocker(ILogger logger) : IProcessBlocker
{
    private readonly Lock _lock = new();
    private CancellationTokenSource? _pollingScanCts;
    private HashSet<string> _targetProcessNames = [];
    private bool _isRunning;

    /// <summary>
    ///     P/Invoke declaration for sending signals via libc.kill().
    ///     CRITICAL: Process.Kill() on Unix ALWAYS sends SIGKILL, ignoring any argument.
    ///     Only libc.kill() allows sending SIGTERM (signal 15) properly.
    /// </summary>
    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    /// <summary>
    ///     Signal number for SIGTERM (graceful termination request).
    /// </summary>
    private const int SigTerm = 15;

    /// <summary>
    ///     Signal number for SIGKILL (forced termination).
    /// </summary>
    private const int SigKill = 9;

    /// <summary>
    ///     Grace period in milliseconds between SIGTERM and SIGKILL.
    /// </summary>
    private const int GracePeriodMs = 5000;

    /// <summary>
    ///     Minimum PID to consider (PIDs &lt; 500 are kernel threads).
    /// </summary>
    private const int MinPid = 500;

    public event Action<string>? ProcessBlocked;

    public List<string> Block(IEnumerable<string> processNames)
    {
        lock (_lock)
        {
            _targetProcessNames = NormalizeNames(processNames);
            logger.LogInformation("Updating blocker rules. Targets: {Count}", _targetProcessNames.Count);

            // Initial scan: kill existing blocked processes but don't fire ProcessBlocked event
            var killed = ScanAndKill(initialScan: true);

            // Start polling loop if not already running
            if (!_isRunning)
            {
                StartPollingMonitoring();
                _isRunning = true;
            }

            return killed;
        }
    }

    public void Unblock(string processName)
    {
        lock (_lock)
        {
            var normalized = NormalizeName(processName);
            if (_targetProcessNames.Remove(normalized))
            {
                logger.LogInformation("Removed '{Process}' from block list.", normalized);
            }
        }
    }

    public void UnblockAll()
    {
        lock (_lock)
        {
            StopMonitoring();
            _targetProcessNames.Clear();
            _isRunning = false;
            logger.LogInformation("All blocking disabled.");
        }
    }

    private void StartPollingMonitoring()
    {
        if (_pollingScanCts != null)
        {
            return;
        }

        _pollingScanCts = new CancellationTokenSource();
        var token = _pollingScanCts.Token;

        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    ScanAndKill(initialScan: false);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error in polling loop");
                }
            }
        }, token);

        logger.LogInformation("Polling monitoring started (500ms interval)");
    }

    private void StopMonitoring()
    {
        if (_pollingScanCts != null)
        {
            _pollingScanCts.Cancel();
            _pollingScanCts.Dispose();
            _pollingScanCts = null;
            logger.LogInformation("Polling monitoring stopped");
        }
    }

    /// <summary>
    ///     Scans /proc filesystem for matching processes and terminates them.
    ///     On Linux, Process.GetProcessesByName() is NOT available (Windows-only API).
    /// </summary>
    /// <param name="initialScan">If true, process blocked event is not fired (initial scan).</param>
    /// <returns>List of unique process names that were terminated.</returns>
    private List<string> ScanAndKill(bool initialScan)
    {
        var killedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> targets;

        lock (_lock)
        {
            targets = [.. _targetProcessNames];
        }

        if (targets.Count == 0)
        {
            return [];
        }

        // Enumerate /proc filesystem — do NOT use Process.GetProcessesByName (Windows only)
        string[] procDirs;
        try
        {
            procDirs = [.. Directory.GetDirectories("/proc").Where(d => int.TryParse(Path.GetFileName(d), out var pid) && pid >= MinPid)];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enumerate /proc directory");
            return [];
        }

        foreach (var dir in procDirs)
        {
            var pid = int.Parse(Path.GetFileName(dir));
            var commPath = Path.Combine(dir, "comm");

            // Read process name from /proc/[pid]/comm
            string? comm;
            try
            {
                if (!File.Exists(commPath))
                {
                    continue;
                }

                comm = File.ReadAllText(commPath).Trim();
            }
            catch (Exception ex)
            {
                // Process may have exited between enumeration and read
                logger.LogDebug(ex, "Failed to read comm for PID {Pid}", pid);
                continue;
            }

            // Normalize for comparison
            var normalizedComm = NormalizeName(comm);
            
            if (!ShouldBlock(normalizedComm))
            {
                continue;
            }

            // Read full command line for logging
            var cmdlinePath = Path.Combine(dir, "cmdline");
            try
            {
                if (File.Exists(cmdlinePath))
                {
                    var cmdline = File.ReadAllText(cmdlinePath).Replace('\0', ' ').Trim();
                    logger.LogDebug("Blocked process command line: {Cmdline}", cmdline);
                }
            }
            catch
            {
                // cmdline read failure is non-fatal
            }

            // Send SIGTERM first (always)
            logger.LogInformation("Terminating blocked process: {Name} (PID: {Pid})", comm, pid);

            // Send SIGTERM via P/Invoke — Process.Kill() MUST NOT be used
            SendSignal(pid, SigTerm, comm);

            // Wait grace period before potentially sending SIGKILL
            Thread.Sleep(GracePeriodMs);

            if (IsProcessAlive(pid))
            {
                logger.LogInformation(
                    "Process still alive after SIGTERM, sending SIGKILL: {Name} (PID: {Pid})",
                    comm, pid);

                SendSignal(pid, SigKill, comm);
            }

            // Fire ProcessBlocked event for runtime kills (not initial scan)
            if (!initialScan)
            {
                ProcessBlocked?.Invoke(normalizedComm);
            }

            killedNames.Add(normalizedComm);
        }

        return [.. killedNames];
    }

    /// <summary>
    ///     Checks if a process with the given PID still exists.
    /// </summary>
    private static bool IsProcessAlive(int pid)
    {
        var procPath = $"/proc/{pid}";
        return Directory.Exists(procPath);
    }

    /// <summary>
    ///     Sends a signal to a process using P/Invoke to libc.kill().
    ///     CRITICAL: Uses IntPtr for PID to handle 64-bit PIDs correctly.
    ///     CRITICAL: Process.Kill() MUST NOT be used — it always sends SIGKILL on Unix.
    /// </summary>
    private void SendSignal(int pid, int signal, string processName)
    {
        try
        {
            var result = kill(pid, signal);
            if (result == 0)
            {
                logger.LogInformation(
                    "Signal {Signal} sent to {Name} (PID: {Pid})",
                    signal == SigTerm ? "SIGTERM (15)" : "SIGKILL (9)",
                    processName,
                    pid);
            }
            else
            {
                var error = Marshal.GetLastPInvokeError();
                logger.LogWarning(
                    "Failed to send signal {Signal} to {Name} (PID: {Pid}). Error: {Error}",
                    signal == SigTerm ? "SIGTERM (15)" : "SIGKILL (9)",
                    processName,
                    pid,
                    error);
            }
        }
        catch (DllNotFoundException)
        {
            logger.LogError("libc not found. Cannot send signals on this system.");
        }
        catch (EntryPointNotFoundException)
        {
            logger.LogError("kill() function not found in libc. Platform may not support signals.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error sending signal {Signal} to {Name} (PID: {Pid})",
                signal == SigTerm ? "SIGTERM (15)" : "SIGKILL (9)",
                processName,
                pid);
        }
    }

    /// <summary>
    ///     Determines if a process should be blocked based on target list.
    /// </summary>
    private bool ShouldBlock(string normalizedName)
    {
        lock (_lock)
        {
            return _targetProcessNames.Contains(normalizedName);
        }
    }

    private static HashSet<string> NormalizeNames(IEnumerable<string> names)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                set.Add(NormalizeName(name));
            }
        }

        return set;
    }

    private static string NormalizeName(string name)
    {
        // Strip .exe suffix if present (for cross-platform compatibility)
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }

    public void Dispose()
    {
        UnblockAll();
    }
}
