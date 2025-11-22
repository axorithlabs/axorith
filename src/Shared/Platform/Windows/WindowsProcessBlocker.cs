using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Windows;

/// <summary>
///     Windows implementation of IProcessBlocker using WMI events.
///     Monitors process creation (__InstanceCreationEvent) to block apps in real-time.
///     Operates strictly in BlockList mode.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsProcessBlocker(ILogger logger) : IProcessBlocker
{
    private readonly Lock _lock = new();
    
    private ManagementEventWatcher? _watcher;
    private HashSet<string> _targetProcessNames = [];

    // Hardcoded safe list to prevent accidental blocking of Axorith itself or critical system components
    // even if user adds them to the blocklist by mistake.
    private static readonly HashSet<string> SafeList = new(StringComparer.OrdinalIgnoreCase)
    {
        "Axorith.Client", "Axorith.Host", "Axorith.Shim", "Axorith.Core",
        "explorer", "taskmgr", "dwm", "lsass", "csrss", "svchost", "winlogon", "services", "spoolsv", "System", "Idle"
    };

    public void Block(IEnumerable<string> processNames)
    {
        lock (_lock)
        {
            _targetProcessNames = NormalizeNames(processNames);

            logger.LogInformation("Updating blocker rules. Targets: {Count}", _targetProcessNames.Count);

            ScanAndKillExisting();

            StartWatcher();
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
            StopWatcher();
            _targetProcessNames.Clear();
            logger.LogInformation("All blocking disabled.");
        }
    }

    private void StartWatcher()
    {
        if (_watcher != null) return;

        try
        {
            var query = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += OnProcessCreated;
            _watcher.Start();
            logger.LogDebug("WMI Process Watcher started (Real-time blocking active).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start WMI Process Watcher. Real-time blocking may not work.");
        }
    }

    private void StopWatcher()
    {
        if (_watcher == null) return;

        try
        {
            _watcher.Stop();
            _watcher.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error stopping WMI watcher.");
        }
        finally
        {
            _watcher = null;
        }
    }

    private void OnProcessCreated(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent["TargetInstance"] is not ManagementBaseObject targetInstance) return;

            var processName = targetInstance["Name"]?.ToString();
            var processIdObj = targetInstance["ProcessId"];

            if (string.IsNullOrEmpty(processName) || processIdObj == null) return;

            var pid = Convert.ToInt32(processIdObj);
            var normalizedName = NormalizeName(processName);

            // Thread-safe check against the target list
            bool shouldBlock;
            lock (_lock)
            {
                shouldBlock = ShouldBlock(normalizedName);
            }

            if (shouldBlock)
            {
                KillProcessById(pid, normalizedName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing WMI event.");
        }
    }

    private void ScanAndKillExisting()
    {
        var processes = Process.GetProcesses();
        foreach (var p in processes)
        {
            try
            {
                var normalizedName = NormalizeName(p.ProcessName);
                if (ShouldBlock(normalizedName))
                {
                    KillProcessById(p.Id, normalizedName, p);
                }
            }
            catch
            {
                // Ignore access denied
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private bool ShouldBlock(string normalizedName)
    {
        if (SafeList.Contains(normalizedName)) return false;
        return _targetProcessNames.Contains(normalizedName);
    }

    private void KillProcessById(int pid, string name, Process? existingInstance = null)
    {
        try
        {
            var p = existingInstance;
            var dispose = false;
            
            if (p == null)
            {
                p = Process.GetProcessById(pid);
                dispose = true;
            }

            try
            {
                if (existingInstance == null && NormalizeName(p.ProcessName) != name) return;

                p.Kill();
                logger.LogInformation("Blocked process: {Name} (PID: {Pid})", name, pid);
            }
            finally
            {
                if (dispose) p.Dispose();
            }
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
        catch (System.ComponentModel.Win32Exception)
        {
            logger.LogWarning("Could not kill process '{Name}' (PID: {Pid}). Access Denied. Try running Axorith as Administrator.", name, pid);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to kill process {Name} (PID: {Pid})", name, pid);
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
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(name);
        }
        return name;
    }

    public void Dispose()
    {
        UnblockAll();
    }
}