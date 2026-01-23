using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal class WindowsPlatformProcessService : IPlatformProcessService
{
    public List<Process> FindProcesses(string processNameOrPath)
    {
        return WindowApi.FindProcesses(processNameOrPath);
    }

    public bool IsProcessRunning(string processNameOrPath)
    {
        return FindProcesses(processNameOrPath).Count > 0;
    }

    public bool IsProcessRunningByName(string processName)
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