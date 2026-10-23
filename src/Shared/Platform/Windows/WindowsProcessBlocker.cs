using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal class WindowsProcessBlocker(ILogger logger) : IProcessBlocker
{
    private readonly Lock _lock = new();
    private readonly int _sessionId = Process.GetCurrentProcess().SessionId;
    private CancellationTokenSource? _pollingScanCts;
    private HashSet<string> _targetProcessNames = [];
    private bool _allowOnlyMode;
    private readonly ConcurrentDictionary<string, string> _originalExecutableNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly WindowsAppDiscoveryService _appDiscovery = new(NullLogger<WindowsAppDiscoveryService>.Instance);

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
                return _pollingScanCts != null;
            }
        }
    }

    public List<string> Block(IEnumerable<string> processNames) => ApplyRules(processNames, allowOnly: false);

    public List<string> Block(IEnumerable<string> processNames, bool rejectRunning) =>
        ApplyRules(processNames, allowOnly: false, rejectRunning);

    public List<string> AllowOnly(IEnumerable<string> processNames) =>
        ApplyRules(processNames, allowOnly: true, rejectRunning: true);

    public List<string> AllowOnly(IEnumerable<string> processNames, bool rejectRunning) =>
        ApplyRules(processNames, allowOnly: true, rejectRunning);

    private List<string> ApplyRules(IEnumerable<string> processNames, bool allowOnly, bool rejectRunning = false)
    {
        lock (_lock)
        {
            StopMonitoring();
            _originalExecutableNames.Clear();
            _targetProcessNames = processNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => NormalizeName(name.Trim()))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (allowOnly && _targetProcessNames.Count == 0)
                throw new ArgumentException("At least one workspace application must be allowed.", nameof(processNames));

            _allowOnlyMode = allowOnly;
            logger.LogInformation(allowOnly
                ? "Applying workspace allowlist with {Count} applications."
                : "Updating blocker rules. Targets: {Count}", _targetProcessNames.Count);

            if (allowOnly && rejectRunning)
            {
                var conflicts = ScanAndKillOutsideAllowlist(initialScan: true, checkOnly: true);
                if (conflicts.Count > 0)
                    throw new InvalidOperationException("Only allow Workspace apps cannot start. Save your work and close these apps first: " +
                                                        string.Join(", ", conflicts.Order(StringComparer.OrdinalIgnoreCase)));
            }

            if (rejectRunning && !allowOnly)
            {
                var conflicts = ScanAndKillByList(initialScan: true, checkOnly: true);
                conflicts.AddRange(ScanAndKillRenamedProcesses(initialScan: true, checkOnly: true));
                if (conflicts.Count > 0)
                    throw new InvalidOperationException("Save your work and finish or close these tools before starting Strict protection: " +
                                                        string.Join(", ", conflicts.Distinct(StringComparer.OrdinalIgnoreCase)
                                                            .Order(StringComparer.OrdinalIgnoreCase)));
            }

            var killed = allowOnly || rejectRunning ? [] : ScanAndKillByList(initialScan: true);
            if (!allowOnly && !rejectRunning)
                killed.AddRange(ScanAndKillRenamedProcesses(initialScan: true));

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

    private void StartPollingMonitoring()
    {
        if (_pollingScanCts != null) return;

        _pollingScanCts = new CancellationTokenSource();
        var token = _pollingScanCts.Token;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            var renamedScanCounter = 0;
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    lock (_lock)
                    {
                        // Stop/reconfigure cannot return while an old scan is still applying rules.
                        if (token.IsCancellationRequested) break;
                        try
                        {
                            if (_allowOnlyMode)
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
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Rule removal normally cancels the pending timer tick.
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

    private List<string> ScanAndKillByList(bool initialScan = false, bool checkOnly = false)
    {
        if (IsAllowOnlyMode()) return ScanAndKillOutsideAllowlist(initialScan, checkOnly);

        var killedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> targets;

        lock (_lock)
        {
            targets = [.. _targetProcessNames];
        }

        foreach (var target in targets)
        {
            if (SafeList.Contains(target)) continue;

            var processes = Process.GetProcessesByName(target);
            foreach (var p in processes)
            {
                try
                {
                    if (p.SessionId == _sessionId && !p.HasExited)
                    {
                        if (checkOnly)
                        {
                            killedNames.Add(target);
                            continue;
                        }

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

    private List<string> ScanAndKillRenamedProcesses(bool initialScan, bool checkOnly = false)
    {
        HashSet<string> targets;
        lock (_lock)
        {
            targets = new HashSet<string>(_targetProcessNames, StringComparer.OrdinalIgnoreCase);
        }

        if (targets.Count == 0) return [];

        var killedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.SessionId != _sessionId) continue;
                var processName = NormalizeName(process.ProcessName);
                if (SafeList.Contains(processName) || targets.Contains(processName)) continue;

                var path = process.MainModule?.FileName;
                var originalName = FindBlockedTarget(processName, path);
                if (originalName == null || process.HasExited) continue;

                if (checkOnly)
                {
                    killedNames.Add(originalName);
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

    private List<string> ScanAndKillOutsideAllowlist(bool initialScan, bool checkOnly = false)
    {
        HashSet<string> allowedNames;
        lock (_lock)
        {
            allowedNames = new HashSet<string>(_targetProcessNames, StringComparer.OrdinalIgnoreCase);
        }

        if (allowedNames.Count == 0) return [];

        using var currentProcess = Process.GetCurrentProcess();
        var currentSessionId = currentProcess.SessionId;
        var parentIds = GetProcessParentIds();
        var processes = Process.GetProcesses();
        var sessionProcesses = new Dictionary<int, Process>();
        HashSet<int> allowedIds = [];
        var childrenByParent = new Dictionary<int, List<int>>();
        var killedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId != currentSessionId) continue;

                    sessionProcesses[process.Id] = process;
                    var processName = NormalizeName(process.ProcessName);
                    if (allowedNames.Contains(processName))
                    {
                        allowedIds.Add(process.Id);
                        continue;
                    }

                    var originalName = GetOriginalExecutableName(process.MainModule?.FileName);
                    if (allowedNames.Contains(originalName))
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
                if (!sessionProcesses.ContainsKey(processId) || !sessionProcesses.ContainsKey(parentId)) continue;

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
                if (!childrenByParent.TryGetValue(allowedParent, out var children)) continue;

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
                    var processName = NormalizeName(process.ProcessName);
                    if (allowedIds.Contains(processId) || SafeList.Contains(processName) ||
                        AllowlistSupportProcesses.Contains(processName) || process.HasExited) continue;

                    if (checkOnly)
                    {
                        killedNames.Add(processName);
                        continue;
                    }

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
            if (!Process32First(snapshot, ref entry)) return result;

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
        if (SafeList.Contains(normalizedName)) return null;

        HashSet<string> targets;
        lock (_lock)
        {
            if (_targetProcessNames.Contains(normalizedName)) return normalizedName;

            targets = new HashSet<string>(_targetProcessNames, StringComparer.OrdinalIgnoreCase);
        }

        var originalName = GetOriginalExecutableName(imagePath);
        return !string.IsNullOrEmpty(originalName) && !SafeList.Contains(originalName) &&
               targets.Contains(originalName)
            ? originalName
            : null;
    }

    private string GetOriginalExecutableName(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return string.Empty;

        return _originalExecutableNames.GetOrAdd(imagePath, path =>
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                var originalFileName = Path.GetFileName(version.OriginalFilename);
                if (string.IsNullOrWhiteSpace(originalFileName) || string.IsNullOrWhiteSpace(version.ProductName))
                    return string.Empty;

                var originalName = NormalizeName(originalFileName);
                if (!_targetProcessNames.Contains(originalName)) return string.Empty;

                // Electron apps share chrome.exe metadata; confirm the product before treating an app as renamed.
                var candidates = _appDiscovery.GetInstalledApplicationsIndex()
                    .Select(app => app.ExecutablePath)
                    .Prepend(Path.Combine(Environment.SystemDirectory, originalFileName))
                    .Prepend(Path.Combine(Path.GetDirectoryName(path)!, originalFileName));
                foreach (var candidate in candidates)
                {
                    if (!Path.GetFileName(candidate).Equals(originalFileName, StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(candidate)) continue;

                    var known = FileVersionInfo.GetVersionInfo(candidate);
                    if (string.Equals(version.ProductName, known.ProductName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(version.CompanyName, known.CompanyName, StringComparison.OrdinalIgnoreCase))
                        return originalName;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        });
    }

    private static string NormalizeName(string name)
    {
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }

    public void Dispose() => UnblockAll();
}
