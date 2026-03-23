using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.MacOS;

/// <summary>
///     macOS process blocker using libproc for process enumeration and libc kill() for termination.
///     - Enumerates processes via proc_listpids/proc_pidinfo from libproc.dylib
///     - Sends SIGKILL immediately (no SIGTERM → SIGKILL like Linux)
///     - Skips PIDs &lt; 100 (kernel/launchd processes)
///     - Safe list: kernel_task, launchd, WindowServer, Finder
///     - 500ms polling interval via PeriodicTimer
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacOsProcessBlocker(ILogger logger) : IProcessBlocker
{
	private readonly Lock _lock = new();
	private CancellationTokenSource? _pollingScanCts;
	private HashSet<string> _targetProcessNames = [];
	private bool _isRunning;

	private const string LibProc = "/usr/lib/libproc.dylib";

	[DllImport(LibProc, SetLastError = true)]
	private static extern int proc_listpids(uint type, uint typeinfo, int[] buffer, int buffersize);

	[DllImport(LibProc, SetLastError = true)]
	private static extern int proc_pidinfo(int pid, int flavor, ulong arg, IntPtr buffer, int buffersize);
	[DllImport("libSystem.B.dylib", SetLastError = true)]
	private static extern int kill(int pid, int sig);
    
	/// <summary>proc_listpids type: return all PIDs.</summary>
	private const uint ProcAllPids = 1;

	/// <summary>proc_pidinfo flavor: returns ProcBsdInfo struct.</summary>
	private const int ProcPidTbsdinfo = 4;

	/// <summary>SIGKILL (signal 9) — forced immediate termination.</summary>
	private const int SigKill = 9;

	/// <summary>Minimum PID to consider (PIDs &lt; 100 are kernel/launchd).</summary>
	private const int MinPid = 100;

	/// <summary>Process names that must never be terminated.</summary>
	private static readonly HashSet<string> SafeList = new(StringComparer.OrdinalIgnoreCase)
	{
		"kernel_task",
		"launchd",
		"WindowServer",
		"Finder"
	};
    
	public event Action<string>? ProcessBlocked;

	public List<string> Block(IEnumerable<string> processNames)
	{
		lock (_lock)
		{
			_targetProcessNames = NormalizeNames(processNames);
			logger.LogInformation("Updating blocker rules. Targets: {Count}", _targetProcessNames.Count);

			var killed = ScanAndKill(initialScan: true);

            if (_isRunning)
            {
                return killed;
            }

            StartPollingMonitoring();
            _isRunning = true;

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
	///     Enumerates all running processes via libproc and terminates blocked ones.
	/// </summary>
	/// <param name="initialScan">If true, ProcessBlocked event is not fired.</param>
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

		var pids = GetAllPids();
		if (pids.Length == 0)
		{
			return [];
		}

		var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<ProcBsdInfo>());
		try
		{
			foreach (var pid in pids)
			{
				if (pid < MinPid)
				{
					continue;
				}

				var processName = GetProcessName(pid, buffer);
				if (string.IsNullOrEmpty(processName))
				{
					continue;
				}

				if (SafeList.Contains(processName))
				{
					logger.LogDebug("Skipping safe process: {Name} (PID: {Pid})", processName, pid);
					continue;
				}

				var normalized = NormalizeName(processName);
				if (!ShouldBlock(normalized))
				{
					continue;
				}

				// Send SIGKILL immediately (no SIGTERM → SIGKILL per macOS design decision)
				logger.LogInformation("Terminating blocked process: {Name} (PID: {Pid})", processName, pid);
				SendSigKill(pid, processName);

				if (!initialScan)
				{
					ProcessBlocked?.Invoke(normalized);
				}

				killedNames.Add(normalized);
			}
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}

		return [.. killedNames];
	}

	/// <summary>
	///     Gets all current PIDs via proc_listpids.
	/// </summary>
	private static int[] GetAllPids()
	{
		// Initial buffer for up to 4096 PIDs
		var bufferSize = 4096;
		var pids = new int[bufferSize];

		var count = proc_listpids(ProcAllPids, 0, pids, pids.Length * sizeof(int));
		if (count <= 0)
		{
			return [];
		}

		var pidCount = count / sizeof(int);
		if (pidCount > bufferSize)
		{
			// Buffer was too small, reallocate and retry
			pids = new int[pidCount];
			count = proc_listpids(ProcAllPids, 0, pids, pids.Length * sizeof(int));
			pidCount = count > 0 ? count / sizeof(int) : 0;
		}

		return pids[..pidCount];
	}

	/// <summary>
	///     Retrieves the process name for a given PID using proc_pidinfo with PROC_PIDTBSDINFO.
	/// </summary>
	private static string? GetProcessName(int pid, IntPtr buffer)
	{
		var structSize = Marshal.SizeOf<ProcBsdInfo>();
		var bytesWritten = proc_pidinfo(pid, ProcPidTbsdinfo, 0, buffer, structSize);
		if (bytesWritten < structSize)
		{
			return null;
		}

		var info = Marshal.PtrToStructure<ProcBsdInfo>(buffer);
		var name = info.PbiName;

		return string.IsNullOrEmpty(name) ? null : name;
	}

	/// <summary>
	///     Sends SIGKILL to a process via libc kill().
	/// </summary>
	private void SendSigKill(int pid, string processName)
	{
		try
		{
			var result = kill(pid, SigKill);
			if (result == 0)
			{
				logger.LogInformation("SIGKILL (9) sent to {Name} (PID: {Pid})", processName, pid);
			}
			else
			{
				var error = Marshal.GetLastPInvokeError();
				logger.LogWarning(
					"Failed to send SIGKILL to {Name} (PID: {Pid}). Error: {Error}",
					processName, pid, error);
			}
		}
		catch (DllNotFoundException)
		{
			logger.LogError("libSystem.B.dylib not found. Cannot send signals on this system.");
		}
		catch (EntryPointNotFoundException)
		{
			logger.LogError("kill() function not found. Platform may not support signals.");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Unexpected error sending SIGKILL to {Name} (PID: {Pid})", processName, pid);
		}
	}

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
		return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
			? Path.GetFileNameWithoutExtension(name)
			: name;
	}

	public void Dispose()
	{
		UnblockAll();
	}
    
	/// <summary>
	///     Maps to struct proc_bsdinfo from macOS libproc.h.
	///     Used with proc_pidinfo(PROC_PIDTBSDINFO) to get process metadata.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
	private struct ProcBsdInfo
	{
		public uint PbiFlags;
		public int PbiStatus;
		public int PbiXstatus;
		public uint PbiPid;
		public uint PbiPpid;
		public uint PbiUid;
		public uint PbiGid;
		public uint PbiRuid;
		public uint PbiRgid;
		public uint PbiSvuid;
		public uint PbiSvgid;
		public uint Rfu_1;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
		public string PbiName; // MAXCOMLEN = 16

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 83)]
		public string PbiComm; // MAXCOMLEN * 2 + 1 = 83 (unused but required for layout)

		public uint PbiNfiles;
		public uint PbiPgid;
		public uint PbiPjobc;
		public uint E_tdev;
		public uint E_tpgid;
		public int PbiNice;
		public ulong PbiStart_tvsec;
		public ulong PbiStart_tvusec;
	}
}
