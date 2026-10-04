using System.Diagnostics;

namespace Axorith.Shared.Platform;

public interface IPlatformProcessService
{
    List<Process> FindProcesses(string processNameOrPath);

    bool IsProcessRunning(string processNameOrPath) => FindProcesses(processNameOrPath).Count > 0;

    bool IsProcessRunningByName(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
