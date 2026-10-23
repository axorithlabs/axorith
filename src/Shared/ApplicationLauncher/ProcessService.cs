using System.Diagnostics;
using Axorith.Sdk.Logging;
using Axorith.Shared.Platform;
using Axorith.Telemetry;

namespace Axorith.Shared.ApplicationLauncher;

public sealed class ProcessService(IModuleLogger logger, IPlatformProcessService processService)
{
    public async Task<ProcessStartResult> StartAsync(ProcessConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        logger.LogInfo("Starting process {Path} with mode {Mode}", config.ApplicationPath, config.StartMode);

        if (config.StartMode is ProcessStartMode.AttachExisting or ProcessStartMode.LaunchOrAttach)
        {
            var existing = await AttachToExistingAsync(config.ApplicationPath).ConfigureAwait(false);
            if (config.StartMode == ProcessStartMode.AttachExisting)
                return new ProcessStartResult(existing, existing != null);
            if (existing != null)
            {
                logger.LogInfo("Attached to existing process {ProcessName} (PID: {ProcessId})",
                    existing.ProcessName, existing.Id);
                return new ProcessStartResult(existing, true);
            }
        }

        var launched = await LaunchNewAsync(config.ApplicationPath, config.Arguments, config.WorkingDirectory)
            .ConfigureAwait(false);
        return new ProcessStartResult(launched, false);
    }

    public Task TerminateAsync(Process process, ProcessLifecycleMode lifecycleMode, bool wasAttached)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (wasAttached || lifecycleMode == ProcessLifecycleMode.KeepRunning)
        {
            logger.LogInfo("Keeping process {ProcessName} (PID: {ProcessId}) running",
                process.ProcessName, process.Id);
            return Task.CompletedTask;
        }

        logger.LogInfo("Terminating process {ProcessName} (PID: {ProcessId}) with mode {Mode}",
            process.ProcessName, process.Id, lifecycleMode);

        try
        {
            if (lifecycleMode == ProcessLifecycleMode.TerminateGraceful)
            {
                // Graceful termination: only close window, never force kill - process may remain open
                logger.LogDebug("Attempting graceful termination (no force kill)");
                process.CloseMainWindow();
                logger.LogDebug("CloseMainWindow called, process may remain open if it shows dialogs");
                logger.LogInfo("Process {ProcessName} termination requested (graceful mode - may remain open)",
                    process.ProcessName);
            }
            else // TerminateForce
            {
                // Force termination: kill immediately without attempting graceful close
                logger.LogDebug("Force killing process immediately");
                try
                {
                    process.Kill();
                    logger.LogInfo("Process {ProcessName} killed immediately", process.ProcessName);
                }
                catch (Exception killEx)
                {
                    logger.LogWarning("Failed to kill process immediately, attempting graceful close as fallback. {ex}",
                        killEx);
                    // Fallback: try graceful close if kill fails
                    process.CloseMainWindow();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to terminate process");
        }

        return Task.CompletedTask;
    }

    public Task<Process?> AttachExistingOnlyAsync(string path) => AttachToExistingAsync(path);

    private Task<Process?> LaunchNewAsync(string path, string args, string? workingDirectory)
    {
        try
        {
            logger.LogDebug("Launching process: {Path} {Args} (WorkingDir: {WorkingDirectory})",
                TelemetryGuard.SafePath(path), args,
                string.IsNullOrWhiteSpace(workingDirectory) ? "<default>" : workingDirectory);

            if (!File.Exists(path))
            {
                logger.LogError(null, "Executable not found: {Path}", TelemetryGuard.SafePath(path));
                return Task.FromResult<Process?>(null);
            }

            var effectiveWorkingDirectory = workingDirectory;

            if (string.IsNullOrWhiteSpace(effectiveWorkingDirectory))
            {
                try
                {
                    effectiveWorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty;
                }
                catch
                {
                    effectiveWorkingDirectory = string.Empty;
                }
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            if (!string.IsNullOrWhiteSpace(effectiveWorkingDirectory))
            {
                startInfo.WorkingDirectory = effectiveWorkingDirectory;
                logger.LogDebug("Using working directory {WorkingDirectory} for process {Path}",
                    TelemetryGuard.SafePath(effectiveWorkingDirectory), TelemetryGuard.SafePath(path));
            }

            var process = processService.StartProcess(startInfo);
            if (process == null)
            {
                logger.LogError(null, "Process.Start() returned false");
                return Task.FromResult<Process?>(null);
            }

            logger.LogInfo("Process {ProcessName} (PID: {ProcessId}) launched successfully",
                process.ProcessName, process.Id);

            return Task.FromResult<Process?>(process);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch process: {Path}", TelemetryGuard.SafePath(path));
            return Task.FromResult<Process?>(null);
        }
    }

    private Task<Process?> AttachToExistingAsync(string path)
    {
        try
        {
            logger.LogDebug("Searching for existing process: {Path}", TelemetryGuard.SafePath(path));

            var processes = processService.FindProcesses(path);
            if (processes.Count == 0)
            {
                logger.LogDebug("No existing process found for {Path}", TelemetryGuard.SafePath(path));
                return Task.FromResult<Process?>(null);
            }

            var processWithWindow = processes.Find(p => p.MainWindowHandle != IntPtr.Zero);
            var selectedProcess = processWithWindow ?? processes[0];

            logger.LogInfo("Found existing process {ProcessName} (PID: {ProcessId})",
                selectedProcess.ProcessName, selectedProcess.Id);

            return Task.FromResult<Process?>(selectedProcess);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while searching for existing process");
            return Task.FromResult<Process?>(null);
        }
    }
}
