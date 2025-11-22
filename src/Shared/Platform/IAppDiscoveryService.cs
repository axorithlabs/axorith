namespace Axorith.Shared.Platform;

public interface IAppDiscoveryService
{
    string? FindKnownApp(params string[] processNames);

    List<AppInfo> FindAppsByPublisher(string publisherName);

    List<AppInfo> GetInstalledApplicationsIndex();
}

public record AppInfo(string Name, string ExecutablePath, string IconPath);