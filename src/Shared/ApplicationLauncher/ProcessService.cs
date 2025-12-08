using System.Diagnostics;
using Axorith.Sdk.Logging;
using Axorith.Shared.Platform;

namespace Axorith.Shared.ApplicationLauncher;

public sealed class ProcessService(IModuleLogger logger)
{
    public async Task<ProcessStartResult> StartAsync(ProcessConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        logger.LogInfo("Starting process {Path} with mode {Mode}", config.ApplicationPath, config.StartMode);

        switch (config.StartMode)
        {
            case ProcessStartMode.AttachExisting:
            {
                var attached = await AttachToExistingAsync(config.ApplicationPath).ConfigureAwait(false);
                return new ProcessStartResult(attached, attached != null);
            }

            case ProcessStartMode.LaunchOrAttach:
            {
                var existing = await AttachToExistingAsync(config.ApplicationPath).ConfigureAwait(false);
                if (existing != null)
                {
                    logger.LogInfo("Attached to existing process {ProcessName} (PID: {ProcessId})",
                        existing.ProcessName, existing.Id);
                    return new ProcessStartResult(existing, true);
                }

                var launched = await LaunchNewAsync(config.ApplicationPath, config.Arguments, config.WorkingDirectory)
                    .ConfigureAwait(false);
                return new ProcessStartResult(launched, false);
            }

            case ProcessStartMode.LaunchNew:
            default:
            {
                var launched = await LaunchNewAsync(config.ApplicationPath, config.Arguments, config.WorkingDirectory)
                    .ConfigureAwait(false);
                return new ProcessStartResult(launched, false);
            }
        }
    }

    public Task TerminateAsync(Process process, ProcessLifecycleMode lifecycleMode, bool wasAttached)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (lifecycleMode == ProcessLifecycleMode.KeepRunning)
        {
            logger.LogInfo("Keeping process {ProcessName} (PID: {ProcessId}) running",
                process.ProcessName, process.Id);
            return Task.CompletedTask;
        }

        if (wasAttached)
        {
            logger.LogWarning("Process was attached from existing. Closing main window only.");
            try
            {
                process.CloseMainWindow();
                logger.LogInfo("Main window closed for process {ProcessName}", process.ProcessName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to close main window");
            }

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

    public Task<Process?> AttachExistingOnlyAsync(string path)
    {
        return AttachToExistingAsync(path);
    }

    private Task<Process?> LaunchNewAsync(string path, string args, string? workingDirectory)
    {
        try
        {
            logger.LogDebug("Launching process: {Path} {Args} (WorkingDir: {WorkingDirectory})", path, args,
                string.IsNullOrWhiteSpace(workingDirectory) ? "<default>" : workingDirectory);

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
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(effectiveWorkingDirectory))
            {
                startInfo.WorkingDirectory = effectiveWorkingDirectory;
                logger.LogDebug("Using working directory {WorkingDirectory} for process {Path}",
                    effectiveWorkingDirectory, path);
            }

            var process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
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
            logger.LogError(ex, "Failed to launch process: {Path}", path);
            return Task.FromResult<Process?>(null);
        }
    }

    private Task<Process?> AttachToExistingAsync(string path)
    {
        try
        {
            logger.LogDebug("Searching for existing process: {Path}", path);

            var processes = PublicApi.FindProcesses(path);
            if (processes.Count == 0)
            {
                logger.LogDebug("No existing process found for {Path}", path);
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