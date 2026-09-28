using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private bool _allowOnlyMode;
    // ponytail: cache each image path for the blocker lifetime; cap this if sessions launch thousands of distinct executables.
    private readonly ConcurrentDictionary<string, string> _originalExecutableNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _isAdmin;

    private static readonly HashSet<string> SafeList = new(StringComparer.OrdinalIgnoreCase)
    {
        "Axorith.Client", "Axorith.Host", "Axorith.Shim", "Axorith.Core",
        "explorer", "dwm", "lsass", "csrss", "svchost", "winlogon", "services", "spoolsv",
        "System", "Idle"
    };

    private static readonly HashSet<string> AllowlistSupportProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "sihost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "SearchApp", "TextInputHost", "RuntimeBroker", "ApplicationFrameHost", "ctfmon", "conhost",
        "taskhostw", "fontdrvhost", "audiodg", "SecurityHealthSystray", "LockApp"
    };

    private const uint SnapshotProcess = 0x00000002;
    private static readonly IntPtr InvalidSnapshotHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    public event Action<string>? ProcessBlocked;

    public bool IsMonitoring
    {
        get
        {
            lock (_lock)
            {
                return _etwSession != null || _pollingScanCts != null;
            }
        }
    }

    public List<string> Block(IEnumerable<string> processNames)
    {
        lock (_lock)
        {
            StopMonitoring();
            _targetProcessNames = NormalizeNames(processNames);
            _allowOnlyMode = false;
            logger.LogInformation("Updating blocker rules. Targets: {Count}", _targetProcessNames.Count);

            var killed = ScanAndKillByList(initialScan: true);
            killed.AddRange(ScanAndKillRenamedProcesses(initialScan: true));

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

    public List<string> AllowOnly(IEnumerable<string> processNames)
    {
        lock (_lock)
        {
            StopMonitoring();
            _targetProcessNames = NormalizeNames(processNames);
            if (_targetProcessNames.Count == 0)
            {
                throw new ArgumentException("At least one workspace application must be allowed.", nameof(processNames));
            }

            _allowOnlyMode = true;
            logger.LogInformation("Applying workspace allowlist with {Count} applications.", _targetProcessNames.Count);
            var killed = ScanAndKillOutsideAllowlist(initialScan: true);
            StartPollingMonitoring();
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
            _allowOnlyMode = false;
            logger.LogInformation("All blocking disabled.");
        }
    }

    private void StartEtwMonitoring()
    {
        if (_etwSession != null)
        {
            return;
        }

        try
        {
            var sessionName = "AxorithProcessBlocker-" + Guid.NewGuid();
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

                var blockedTarget = FindBlockedTarget(normalized, imagePath);
                if (blockedTarget != null)
                {
                    if (string.Equals(normalized, blockedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        Task.Run(() => KillProcessWithValidation(pid, normalized, blockedTarget, imagePath));
                    }
                    else
                    {
                        Task.Run(() => CheckStartedImagePath(pid, normalized, imagePath));
                    }
                }
                else if (HasTargets())
                {
                    Task.Run(() => CheckStartedImagePath(pid, normalized, imagePath));
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
            var renamedScanCounter = 0;
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    if (IsAllowOnlyMode())
                    {
                        ScanAndKillOutsideAllowlist(initialScan: false);
                    }
                    else
                    {
                        ScanAndKillByList(initialScan: false);
                        if (++renamedScanCounter == 4)
                        {
                            ScanAndKillRenamedProcesses(initialScan: false);
                            renamedScanCounter = 0;
                        }
                    }
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
        if (IsAllowOnlyMode())
        {
            return ScanAndKillOutsideAllowlist(initialScan);
        }

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

    private List<string> ScanAndKillRenamedProcesses(bool initialScan)
    {
        List<string> targets;
        lock (_lock)
        {
            targets = [.. _targetProcessNames];
        }

        if (targets.Count == 0)
        {
            return [];
        }

        var killedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var processName = NormalizeName(process.ProcessName);
                if (SafeList.Contains(processName) || targets.Contains(processName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = process.MainModule?.FileName;
                var originalName = GetOriginalExecutableName(path);
                if (string.IsNullOrEmpty(originalName) || SafeList.Contains(originalName) ||
                    !targets.Contains(originalName, StringComparer.OrdinalIgnoreCase) || process.HasExited)
                {
                    continue;
                }

                process.Kill();
                logger.LogInformation("Blocked renamed process: {Target} (PID: {Pid}, image: {Image})",
                    originalName, process.Id, path);
                if (!initialScan)
                {
                    ProcessBlocked?.Invoke(originalName);
                }

                killedNames.Add(originalName);
            }
            catch (Win32Exception ex)
            {
                logger.LogDebug("Could not inspect or stop process (PID: {Pid}). Access denied: {Error}",
                    process.Id, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to inspect renamed process (PID: {Pid})", process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }

        return [.. killedNames];
    }

    private List<string> ScanAndKillOutsideAllowlist(bool initialScan)
    {
        string[] allowedNames;
        lock (_lock)
        {
            allowedNames = [.. _targetProcessNames];
        }

        // An empty allowlist must never be interpreted as permission to close the whole desktop.
        if (allowedNames.Length == 0)
        {
            return [];
        }

        using var currentProcess = Process.GetCurrentProcess();
        var currentSessionId = currentProcess.SessionId;
        var parentIds = GetProcessParentIds();
        var processes = Process.GetProcesses();
        var sessionProcesses = new Dictionary<int, Process>();
        var allowedIds = new HashSet<int>();
        var childrenByParent = new Dictionary<int, List<int>>();
        var killedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId != currentSessionId)
                    {
                        continue;
                    }

                    sessionProcesses[process.Id] = process;
                    var processName = NormalizeName(process.ProcessName);
                    if (allowedNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
                    {
                        allowedIds.Add(process.Id);
                        continue;
                    }

                    var originalName = GetOriginalExecutableName(process.MainModule?.FileName);
                    if (allowedNames.Contains(originalName, StringComparer.OrdinalIgnoreCase))
                    {
                        allowedIds.Add(process.Id);
                    }
                }
                catch (Win32Exception ex)
                {
                    logger.LogDebug("Could not inspect process (PID: {Pid}) for allowlist: {Error}", process.Id, ex.Message);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Could not inspect process (PID: {Pid}) for allowlist", process.Id);
                }
            }

            foreach (var (processId, parentId) in parentIds)
            {
                if (!sessionProcesses.ContainsKey(processId) || !sessionProcesses.ContainsKey(parentId))
                {
                    continue;
                }

                if (!childrenByParent.TryGetValue(parentId, out var children))
                {
                    children = [];
                    childrenByParent.Add(parentId, children);
                }

                children.Add(processId);
            }

            var pending = new Queue<int>(allowedIds);
            while (pending.TryDequeue(out var allowedParent))
            {
                if (!childrenByParent.TryGetValue(allowedParent, out var children))
                {
                    continue;
                }

                foreach (var child in children)
                {
                    if (allowedIds.Add(child))
                    {
                        pending.Enqueue(child);
                    }
                }
            }

            foreach (var (processId, process) in sessionProcesses)
            {
                try
                {
                    if (allowedIds.Contains(processId) || SafeList.Contains(NormalizeName(process.ProcessName)) ||
                        AllowlistSupportProcesses.Contains(NormalizeName(process.ProcessName)) ||
                        process.HasExited)
                    {
                        continue;
                    }

                    var processName = NormalizeName(process.ProcessName);
                    process.Kill();
                    logger.LogInformation("Blocked non-workspace process: {Name} (PID: {Pid})", processName, processId);
                    if (!initialScan)
                    {
                        ProcessBlocked?.Invoke(processName);
                    }

                    killedNames.Add(processName);
                }
                catch (Win32Exception ex)
                {
                    logger.LogDebug("Could not stop process '{Name}' (PID: {Pid}). Access denied: {Error}",
                        process.ProcessName, processId, ex.Message);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Could not stop non-workspace process (PID: {Pid})", processId);
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return [.. killedNames];
    }

    private bool IsAllowOnlyMode()
    {
        lock (_lock)
        {
            return _allowOnlyMode;
        }
    }

    private Dictionary<int, int> GetProcessParentIds()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcess, 0);
        if (snapshot == InvalidSnapshotHandle)
        {
            logger.LogWarning("Could not snapshot Windows processes for workspace app child tracking.");
            return result;
        }

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return result;
            }

            do
            {
                result[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    private string? FindBlockedTarget(string normalizedName, string? imagePath)
    {
        if (SafeList.Contains(normalizedName))
        {
            return null;
        }

        string[] targets;
        lock (_lock)
        {
            if (_targetProcessNames.Contains(normalizedName))
            {
                return normalizedName;
            }

            targets = [.. _targetProcessNames];
        }

        var originalName = GetOriginalExecutableName(imagePath);
        return !string.IsNullOrEmpty(originalName) && !SafeList.Contains(originalName) &&
               targets.Contains(originalName, StringComparer.OrdinalIgnoreCase)
            ? originalName
            : null;
    }

    private bool HasTargets()
    {
        lock (_lock)
        {
            return _targetProcessNames.Count > 0;
        }
    }

    private void CheckStartedImagePath(int pid, string expectedName, string? eventImagePath)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!string.Equals(NormalizeName(process.ProcessName), expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var imagePath = process.MainModule?.FileName ?? eventImagePath;
            var blockedTarget = FindBlockedTarget(expectedName, imagePath);
            if (blockedTarget != null)
            {
                KillProcessWithValidation(pid, expectedName, blockedTarget, imagePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not inspect process start image (PID: {Pid})", pid);
        }
    }

    private string GetOriginalExecutableName(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return string.Empty;
        }

        return _originalExecutableNames.GetOrAdd(imagePath, static path =>
        {
            try
            {
                var originalFileName = FileVersionInfo.GetVersionInfo(path).OriginalFilename;
                return string.IsNullOrWhiteSpace(originalFileName)
                    ? string.Empty
                    : NormalizeName(Path.GetFileName(originalFileName));
            }
            catch
            {
                return string.Empty;
            }
        });
    }

    private bool KillProcessWithValidation(int pid, string expectedName, string blockedTarget, string? imagePath)
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

                if (!string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug(
                        "PID {Pid} name mismatch. Expected: {Expected}, Got: {Actual}. Possible PID reuse, skipping.",
                        pid, expectedName, actualName);
                    return false;
                }

                if (!string.Equals(actualName, blockedTarget, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(GetOriginalExecutableName(p.MainModule?.FileName), blockedTarget,
                        StringComparison.OrdinalIgnoreCase))
                {
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
