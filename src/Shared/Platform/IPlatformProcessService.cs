using System.Diagnostics;

namespace Axorith.Shared.Platform;

/// <summary>
///     Cross-platform process discovery and management service.
/// </summary>
public interface IPlatformProcessService
{
    List<Process> FindProcesses(string processNameOrPath);
    bool IsProcessRunning(string processNameOrPath);
    bool IsProcessRunningByName(string processName);
}