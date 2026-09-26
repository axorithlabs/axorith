using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform;
using Axorith.Host.Mappers;
using Moq;
using Xunit;
using LauncherModule = Axorith.Module.ApplicationLauncher.Module;

namespace Axorith.Integrations.Tests;

public sealed class ApplicationLauncherVisibilityTests
{
    [Fact]
    public async Task SpotifySettingsStayHiddenUntilSpotifyIsSelected()
    {
        var discovery = new Mock<IAppDiscoveryService>();
        discovery.Setup(x => x.GetInstalledApplicationsIndex()).Returns([]);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        using var module = new LauncherModule(
            Mock.Of<IModuleLogger>(), discovery.Object, Mock.Of<INotifier>(),
            httpClientFactory.Object, Mock.Of<ISecureStorageService>(),
            Mock.Of<IPlatformProcessService>(), Mock.Of<IPlatformWindowService>());

        await module.InitializeAsync(CancellationToken.None);
        var settings = module.GetSettings().ToDictionary(x => x.Key);

        Assert.Equal(string.Empty, settings["ApplicationPath"].GetValueAsString());
        Assert.False(settings["AuthStatus"].GetCurrentVisibility());

        settings["EnablePlayback"].SetValueFromString("True");
        foreach (var key in new[]
                 {
                     "EnablePlayback", "AuthStatus", "DeviceSelectionMode", "SpecificDeviceName",
                     "PlaybackContext", "CustomUrl", "Volume", "Shuffle", "RepeatMode"
                 })
            Assert.False(settings[key].GetCurrentVisibility());
        Assert.False(SettingMapper.ToMessage(settings["AuthStatus"]).IsVisible);

        settings["ApplicationPath"].SetValueFromString(@"C:\Apps\Spotify.exe");
        Assert.True(settings["AuthStatus"].GetCurrentVisibility());
        Assert.True(SettingMapper.ToMessage(settings["AuthStatus"]).IsVisible);

        settings["ApplicationPath"].SetValueFromString(string.Empty);
        Assert.False(settings["AuthStatus"].GetCurrentVisibility());
    }
}
