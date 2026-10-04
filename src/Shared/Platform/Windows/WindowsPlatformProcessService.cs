using System.Diagnostics;
using System.Runtime.Versioning;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal class WindowsPlatformProcessService : IPlatformProcessService
{
    public List<Process> FindProcesses(string processNameOrPath) => WindowApi.FindProcesses(processNameOrPath);
}
