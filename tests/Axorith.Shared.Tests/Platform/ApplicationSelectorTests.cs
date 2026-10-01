using Axorith.Shared.Platform;
using Xunit;

namespace Axorith.Shared.Tests.Platform;

public sealed class ApplicationSelectorTests
{
    [Fact]
    public void InstalledChoicesFilterUnsupportedAppsAndDeduplicateExecutableNames()
    {
        var appRoot = Path.Combine(Path.GetTempPath(), "AxorithAppChoices");
        var discovery = new TestAppDiscovery(
        [
            new AppInfo("Google Chrome", Path.Combine(appRoot, "Chrome", "chrome.exe"), "chrome.ico"),
            new AppInfo("Chrome Beta", Path.Combine(appRoot, "Chrome Beta", "chrome.exe"), "beta.ico"),
            new AppInfo("Visual Studio Code", Path.Combine(appRoot, "VS Code", "Code.exe"), "code.ico"),
            new AppInfo("Unknown", Path.Combine(appRoot, "Unknown.exe"), "unknown.ico"),
            new AppInfo("Empty path", string.Empty, string.Empty)
        ]);
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDirectory))
            discovery.Applications.Add(new AppInfo("System Browser",
                Path.Combine(windowsDirectory, "System32", "chrome.exe"), "system.ico"));

        var choices = ApplicationSelector.GetInstalledChoices(discovery, app => app.ExecutablePath,
            ApplicationSelector.IsSupportedLauncherApp);

        Assert.Equal(2, choices.Count);
        Assert.Equal(Path.Combine(appRoot, "Chrome Beta", "chrome.exe"), choices[0].Key);
        Assert.Equal(Path.Combine(appRoot, "VS Code", "Code.exe"), choices[1].Key);
        Assert.Contains("Visual Studio Code", choices[1].Value);
        Assert.Contains("code.ico", choices[1].Value);
    }

    [Theory]
    [InlineData("chrome.exe", "Browser")]
    [InlineData("obs64.exe", "OBS")]
    [InlineData("discord.exe", "Discord")]
    [InlineData("code.exe", "VSCode")]
    [InlineData("steam.exe", "Steam")]
    [InlineData("spotify.exe", "Spotify")]
    [InlineData("rider64.exe", "JetBrainsIDE")]
    [InlineData("custom.exe", null)]
    public void ExecutableRoutesToItsLauncherModule(string path, string? expectedModule) =>
        Assert.Equal(expectedModule, ApplicationSelector.GetLauncherModuleKey(path));

    private sealed class TestAppDiscovery : IAppDiscoveryService
    {
        public List<AppInfo> Applications { get; }

        public TestAppDiscovery(List<AppInfo> applications) => Applications = applications;

        public string? FindKnownApp(params string[] processNames) => null;
        public List<AppInfo> FindAppsByPublisher(string publisherName) => [];
        public List<AppInfo> GetInstalledApplicationsIndex() => [.. Applications];
    }
}
