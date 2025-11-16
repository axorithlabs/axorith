using System.Diagnostics;
using Axorith.Sdk.Logging;

namespace Axorith.Module.ApplicationManager;

internal enum LifecycleMode
{
    TerminateOnEnd,
    KeepRunning
}

/// <summary>
///     Service responsible purely for working with System.Diagnostics.Process:
///     launching, attaching to, and terminating processes.
/// </summary>
internal sealed class ProcessService(IModuleLogger logger)
{
    public Task<Process?> LaunchNewAsync(string path, string args)
    {
        try
        {
            logger.LogDebug("Launching process: {Path} {Args}", path, args);

            var workingDirectory = string.Empty;
            try
            {
                workingDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch
            {
                // If Path.GetDirectoryName fails for some reason, fall back to default working directory.
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
                logger.LogDebug("Using working directory {WorkingDirectory} for process {Path}", workingDirectory, path);
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

    public Task<Process?> AttachToExistingAsync(string path)
    {
        try
        {
            logger.LogDebug("Searching for existing process: {Path}", path);

            var processes = Process.GetProcesses();
            var matches = new List<Process>();

            foreach (var process in processes)
                try
                {
                    var fileName = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(fileName) &&
                        string.Equals(fileName, path, StringComparison.OrdinalIgnoreCase))
                        matches.Add(process);
                }
                catch
                {
                    // Access denied or process exited between enumeration and inspection; ignore.
                }

            if (matches.Count == 0)
            {
                logger.LogDebug("No existing process found for {Path}", path);
                return Task.FromResult<Process?>(null);
            }

            var processWithWindow = matches.Find(p => p.MainWindowHandle != IntPtr.Zero);
            var selectedProcess = processWithWindow ?? matches[0];

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

    public Task TerminateAsync(Process process, LifecycleMode mode, bool wasAttached)
    {
        if (mode == LifecycleMode.KeepRunning)
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

        logger.LogInfo("Terminating process {ProcessName} (PID: {ProcessId})",
            process.ProcessName, process.Id);

        try
        {
            if (!process.CloseMainWindow())
            {
                logger.LogDebug("CloseMainWindow failed, waiting 2 seconds before force kill");
                if (!process.WaitForExit(2000))
                {
                    logger.LogWarning("Process did not exit gracefully, forcing termination");
                    process.Kill();
                }
            }
            else
            {
                logger.LogDebug("Main window closed, waiting for process exit");
                process.WaitForExit(3000);
            }

            logger.LogInfo("Process {ProcessName} terminated", process.ProcessName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to terminate process");
        }

        return Task.CompletedTask;
    }
}