using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Windows process blocker with clean architecture:
///     - Admin mode: ETW only (real-time, zero overhead)
///     - User mode: Polling only (simple, reliable fallback)
///     No hybrid chaos - one strategy per privilege level.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsProcessBlocker(ILogger logger) : IProcessBlocker
{
    private readonly Lock _lock = new();
    private TraceEventSession? _etwSession;
    private CancellationTokenSource? _pollingScanCts;
    private HashSet<string> _targetProcessNames = [];
    private bool _isAdmin;

    private static readonly HashSet<string> SafeList = new(StringComparer.OrdinalIgnoreCase)
    {
        "Axorith.Client", "Axorith.Host", "Axorith.Shim", "Axorith.Core",
        "explorer", "taskmgr", "dwm", "lsass", "csrss", "svchost", "winlogon", "services", "spoolsv", "System", "Idle"
    };

    public event Action<string>? ProcessBlocked;

    public List<string> Block(IEnumerable<string> processNames)
    {
        lock (_lock)
        {
            _targetProcessNames = NormalizeNames(processNames);
            logger.LogInformation("Updating blocker rules. Targets: {Count}", _targetProcessNames.Count);

            var killed = ScanAndKillByList(initialScan: true);

            _isAdmin = TraceEventSession.IsElevated() ?? false;

            if (_isAdmin)
            {
                logger.LogInformation("Admin privileges detected. Using ETW for real-time monitoring.");
                StartEtwMonitoring();
            }
            else
            {
                logger.LogInformation("Running as standard user. Using polling-based monitoring.");
                StartPollingMonitoring();
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
            logger.LogInformation("All blocking disabled.");
        }
    }

    private void StartEtwMonitoring()
    {
        if (_etwSession != null)
        {
            return;
        }

        const string sessionName = "AxorithProcessBlocker";

        try
        {
            // Clean up any leftover session from a previous crash (kernel-level sessions survive process death)
            try
            {
                using var existing = TraceEventSession.GetActiveSession(sessionName);
                if (existing != null)
                {
                    existing.Stop();
                    logger.LogInformation("Stopped leftover ETW session from previous run");
                }
            }
            catch
            {
                // No existing session — this is normal on first start
            }

            _etwSession = new TraceEventSession(sessionName);
            _etwSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

            _etwSession.Source.Kernel.ProcessStart += data =>
            {
                var processName = data.ProcessName;
                var pid = data.ProcessID;
                var imagePath = data.ImageFileName;

                if (string.IsNullOrEmpty(processName))
                {
                    return;
                }

                var normalized = NormalizeName(processName);

                bool shouldBlock;
                lock (_lock)
                {
                    shouldBlock = ShouldBlock(normalized);
                }

                if (shouldBlock)
                {
                    Task.Run(() => KillProcessWithValidation(pid, normalized, imagePath));
                }
            };

            Task.Run(() =>
            {
                try
                {
                    _etwSession.Source.Process();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "ETW session processing failed");
                }
            });

            logger.LogInformation("ETW monitoring started successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start ETW session. Falling back to polling.");
            _etwSession?.Dispose();
            _etwSession = null;
            _isAdmin = false;
            StartPollingMonitoring();
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
                    ScanAndKillByList(initialScan: false);
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
        if (_etwSession != null)
        {
            try
            {
                _etwSession.Stop();
                _etwSession.Dispose();
                logger.LogInformation("ETW monitoring stopped");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error stopping ETW session");
            }
            finally
            {
                _etwSession = null;
            }
        }

        if (_pollingScanCts != null)
        {
            _pollingScanCts.Cancel();
            _pollingScanCts.Dispose();
            _pollingScanCts = null;
            logger.LogInformation("Polling monitoring stopped");
        }
    }

    private List<string> ScanAndKillByList(bool initialScan = false)
    {
        var killedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> targets;

        lock (_lock)
        {
            targets = [.. _targetProcessNames];
        }

        foreach (var target in targets)
        {
            if (SafeList.Contains(target))
            {
                continue;
            }

            var processes = Process.GetProcessesByName(target);
            foreach (var p in processes)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        logger.LogInformation("Blocked process: {Name} (PID: {Pid})", target, p.Id);

                        if (!initialScan)
                        {
                            ProcessBlocked?.Invoke(target);
                        }

                        killedNames.Add(target);
                    }
                }
                catch (Win32Exception ex)
                {
                    logger.LogDebug("Could not kill process '{Name}' (PID: {Pid}). Access denied: {Error}",
                        target, p.Id, ex.Message);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to kill process {Name} (PID: {Pid})", target, p.Id);
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        return [.. killedNames];
    }

    private bool ShouldBlock(string normalizedName)
    {
        if (SafeList.Contains(normalizedName))
        {
            return false;
        }

        return _targetProcessNames.Contains(normalizedName);
    }

    private bool KillProcessWithValidation(int pid, string expectedName, string? imagePath)
    {
        try
        {
            Process? p = null;
            try
            {
                p = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return false;
            }

            using (p)
            {
                var actualName = NormalizeName(p.ProcessName);

                if (actualName != expectedName)
                {
                    logger.LogDebug(
                        "PID {Pid} name mismatch. Expected: {Expected}, Got: {Actual}. Possible PID reuse, skipping.",
                        pid, expectedName, actualName);
                    return false;
                }

                if (!string.IsNullOrEmpty(imagePath))
                {
                    try
                    {
                        var processPath = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath) &&
                            !string.Equals(processPath, imagePath, StringComparison.OrdinalIgnoreCase))
                        {
                            logger.LogDebug(
                                "PID {Pid} path mismatch. Expected: {Expected}, Got: {Actual}. Possible PID reuse, skipping.",
                                pid, imagePath, processPath);
                            return false;
                        }
                    }
                    catch
                    {
                        // Access denied or process exited - continue with name-only validation
                    }
                }

                if (p.HasExited)
                {
                    return false;
                }

                p.Kill();
                logger.LogInformation("Blocked process: {Name} (PID: {Pid})", expectedName, pid);
                ProcessBlocked?.Invoke(expectedName);
                return true;
            }
        }
        catch (Win32Exception ex)
        {
            logger.LogDebug("Could not kill process '{Name}' (PID: {Pid}). {Error}", expectedName, pid, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to kill process {Name} (PID: {Pid})", expectedName, pid);
        }

        return false;
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
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }

    public void Dispose()
    {
        UnblockAll();
    }
}