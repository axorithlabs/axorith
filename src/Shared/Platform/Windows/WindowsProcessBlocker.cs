using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Windows implementation of IProcessBlocker.
///     Strategy:
///     1. If Admin -> Use ETW (Real-time, Zero overhead).
///     2. If User  -> Use WinEventHook (Real-time for windows) + Optimized Polling (Background).
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsProcessBlocker(ILogger logger) : IProcessBlocker
{
    private readonly Lock _lock = new();

    private TraceEventSession? _etwSession;
    private Task? _etwTask;

    private CancellationTokenSource? _pollingCts;
    private IntPtr _winEventHook = IntPtr.Zero;
    private WindowApi.WinEventDelegate? _winEventDelegate;

    private HashSet<string> _targetProcessNames = [];

    public event Action<string>? ProcessBlocked;

    private static readonly HashSet<string> SafeList = new(StringComparer.OrdinalIgnoreCase)
    {
        "Axorith.Client", "Axorith.Host", "Axorith.Shim", "Axorith.Core",
        "explorer", "taskmgr", "dwm", "lsass", "csrss", "svchost", "winlogon", "services", "spoolsv", "System", "Idle"
    };

    public List<string> Block(IEnumerable<string> processNames)
    {
        lock (_lock)
        {
            _targetProcessNames = NormalizeNames(processNames);
            logger.LogInformation("Updating blocker rules. Targets: {Count}", _targetProcessNames.Count);

            var killed = ScanAndKillByList(initialScan: true);

            StartMonitoring();

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
                logger.LogInformation("Removed '{Process}' from BlockList.", normalized);
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

    private void StartMonitoring()
    {
        if (_etwSession != null || _pollingCts != null)
        {
            return;
        }

        if (TraceEventSession.IsElevated() ?? false)
        {
            logger.LogInformation("Admin privileges detected. Starting Real-time ETW Monitor.");
            StartEtwSession();
        }
        else
        {
            logger.LogInformation("Running as Standard User. Starting Hybrid Monitor (WinEventHook + Polling).");
            StartWinEventHook();
            StartPollingSession();
        }
    }

    private void StartEtwSession()
    {
        try
        {
            var sessionName = "AxorithProcessBlocker-" + Guid.NewGuid();
            _etwSession = new TraceEventSession(sessionName);

            _etwSession.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

            _etwSession.Source.Kernel.ProcessStart += data =>
            {
                var processName = data.ProcessName;
                var pid = data.ProcessID;
                var imageName = data.ImageFileName;

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
                    Task.Run(() => KillProcessByIdWithValidation(pid, normalized, imageName));
                }
            };

            _etwTask = Task.Run(() =>
            {
                try
                {
                    _etwSession.Source.Process();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "ETW Session processing failed");
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start ETW session. Falling back to hybrid mode.");
            _etwSession?.Dispose();
            _etwSession = null;
            StartWinEventHook();
            StartPollingSession();
        }
    }

    private void StartWinEventHook()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            return;
        }

        _winEventDelegate = WinEventProc;

        _winEventHook = WindowApi.SetWinEventHook(
            WindowApi.EVENT_SYSTEM_FOREGROUND,
            WindowApi.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            WindowApi.WINEVENT_OUTOFCONTEXT);

        if (_winEventHook == IntPtr.Zero)
        {
            logger.LogError("Failed to set WinEventHook");
        }
        else
        {
            logger.LogDebug("WinEventHook installed for foreground window detection.");
        }
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            WindowApi.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
            {
                return;
            }

            try
            {
                using var p = Process.GetProcessById((int)pid);
                var normalized = NormalizeName(p.ProcessName);

                bool shouldBlock;
                lock (_lock)
                {
                    shouldBlock = ShouldBlock(normalized);
                }

                if (!shouldBlock)
                {
                    return;
                }

                logger.LogInformation("WinEventHook detected blocked app: {Name} (PID: {Pid})", normalized, pid);
                p.Kill();
                ProcessBlocked?.Invoke(normalized);
            }
            catch
            {
                // Process might have exited or access denied
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error in WinEventProc");
        }
    }

    private void StartPollingSession()
    {
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

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
    }

    private void StopMonitoring()
    {
        if (_etwSession != null)
        {
            try
            {
                _etwSession.Stop();
                _etwSession.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error stopping ETW session");
            }
            finally
            {
                _etwSession = null;
                _etwTask = null;
            }
        }

        if (_winEventHook != IntPtr.Zero)
        {
            WindowApi.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
            _winEventDelegate = null;
        }

        if (_pollingCts != null)
        {
            _pollingCts.Cancel();
            _pollingCts.Dispose();
            _pollingCts = null;
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
                if (KillProcessById(p.Id, target, p, suppressEvent: initialScan))
                {
                    killedNames.Add(target);
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

    private bool KillProcessById(int pid, string name, Process? existingInstance = null, bool suppressEvent = false)
    {
        try
        {
            var p = existingInstance;
            var dispose = false;

            if (p == null)
            {
                try
                {
                    p = Process.GetProcessById(pid);
                    dispose = true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            try
            {
                if (existingInstance == null && NormalizeName(p.ProcessName) != name)
                {
                    return false;
                }

                if (!p.HasExited)
                {
                    p.Kill();
                    logger.LogInformation("Blocked process: {Name} (PID: {Pid})", name, pid);

                    if (!suppressEvent)
                    {
                        ProcessBlocked?.Invoke(name);
                    }

                    return true;
                }
            }
            catch (Win32Exception)
            {
                logger.LogDebug("Could not kill process '{Name}' (PID: {Pid}). Access Denied.", name, pid);
            }
            finally
            {
                if (dispose)
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to kill process {Name} (PID: {Pid})", name, pid);
        }

        return false;
    }

    private bool KillProcessByIdWithValidation(int pid, string expectedName, string? imagePath)
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
                        // Access denied or process exited, continue with name-only validation
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