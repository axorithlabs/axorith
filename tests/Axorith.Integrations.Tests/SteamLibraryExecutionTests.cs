using System.Diagnostics;
using Axorith.Sdk;
using Axorith.Shared.Platform;
using Moq;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class SteamLibraryExecutionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("480")]
    public async Task SessionStartSendsOnlyTheSelectedGameToTheInjectedWindowsProcessBoundary(string gameId)
    {
        var directory = Directory.CreateTempSubdirectory("axorith-steam-launch-");
        try
        {
            var steam = Path.Combine(directory.FullName, "steam.exe");
            await File.WriteAllTextAsync(steam, "discovery fixture; never executed");
            var discovery = new Mock<IAppDiscoveryService>();
            discovery.Setup(value => value.GetInstalledApplicationsIndex()).Returns([]);
            discovery.Setup(value => value.FindKnownApp(It.IsAny<string[]>())).Returns(steam);
            var requests = new List<ProcessStartInfo>();
            var processes = new Mock<IPlatformProcessService>();
            processes.Setup(value => value.StartProcess(It.IsAny<ProcessStartInfo>()))
                .Returns((ProcessStartInfo start) =>
                {
                    requests.Add(start);
                    return start.UseShellExecute ? null : Process.GetCurrentProcess();
                });
            var windows = new Mock<IPlatformWindowService>();
            windows.Setup(value => value.WaitForWindowInitAsync(It.IsAny<Process>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var clients = new Mock<IHttpClientFactory>();
            clients.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient());
            using var module = new Axorith.Module.ApplicationLauncher.Module(new TestModuleLogger(), discovery.Object,
                new NoopNotifier(), clients.Object, new EmptySecureStorage(), processes.Object, windows.Object);
            await module.InitializeAsync(CancellationToken.None);
            var settings = module.GetSettings().ToDictionary(setting => setting.Key);
            settings["ApplicationPath"].SetValueFromString(steam);
            settings["ProcessMode"].SetValueFromString("LaunchNew");
            settings["LifecycleMode"].SetValueFromString("KeepRunning");
            Assert.Equal(ValidationStatus.Ok, (await module.ValidateSettingsAsync(CancellationToken.None)).Status);
            settings["SelectedGame"].SetValueFromString(gameId);

            await module.OnSessionStartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await module.OnSessionEndAsync(CancellationToken.None);

            Assert.Equal(gameId.Length == 0 ? 1 : 2, requests.Count);
            Assert.Equal(steam, requests[0].FileName);
            Assert.False(requests[0].UseShellExecute);
            if (gameId.Length != 0)
            {
                Assert.Equal("steam://rungameid/480", requests[1].FileName);
                Assert.True(requests[1].UseShellExecute);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task LauncherRefreshReadsAllLibrariesDeduplicatesGamesAndClearsARemovedSelection()
    {
        var directory = Directory.CreateTempSubdirectory("axorith-steam-library-");
        try
        {
            var steam = Path.Combine(directory.FullName, "steam.exe");
            await File.WriteAllTextAsync(steam, "discovery fixture; never executed");
            var primary = Directory.CreateDirectory(Path.Combine(directory.FullName, "steamapps")).FullName;
            var secondaryLibrary = Path.Combine(directory.FullName, "Second library");
            var secondary = Directory.CreateDirectory(Path.Combine(secondaryLibrary, "steamapps")).FullName;
            await File.WriteAllTextAsync(Path.Combine(primary, "libraryfolders.vdf"),
                "\"libraryfolders\" { \"1\" { \"path\" \"" + secondaryLibrary.Replace("\\", "\\\\") + "\" } }");
            await Manifest(primary, "480", "Fixture Game");
            await Manifest(secondary, "480", "Duplicate Game");
            await Manifest(secondary, "481", "Another Game");
            await Manifest(primary, "9000", "Steamworks Common Redistributables");
            await File.WriteAllTextAsync(Path.Combine(primary, "appmanifest_broken.acf"), "not a valid game manifest");
            var discovery = new Mock<IAppDiscoveryService>();
            discovery.Setup(value => value.GetInstalledApplicationsIndex()).Returns([]);
            discovery.Setup(value => value.FindKnownApp(It.IsAny<string[]>())).Returns(steam);
            var clients = new Mock<IHttpClientFactory>();
            clients.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient());
            using var module = new Axorith.Module.ApplicationLauncher.Module(new TestModuleLogger(), discovery.Object,
                new NoopNotifier(), clients.Object, new EmptySecureStorage(), Mock.Of<IPlatformProcessService>(),
                Mock.Of<IPlatformWindowService>());
            await module.InitializeAsync(CancellationToken.None);
            var settings = module.GetSettings().ToDictionary(setting => setting.Key);
            settings["ApplicationPath"].SetValueFromString(steam);
            Assert.Equal(ValidationStatus.Ok, (await module.ValidateSettingsAsync(CancellationToken.None)).Status);
            var refresh = Assert.Single(module.GetActions(), action => action.Key == "Steam.RefreshGames");

            await refresh.InvokeAsync().WaitAsync(TimeSpan.FromSeconds(5));

            var games = settings["SelectedGame"];
            Assert.Equal(["", "481", "480"], games.GetCurrentChoices()!.Select(choice => choice.Key));
            Assert.Equal(["None (just launch Steam)", "Another Game", "Fixture Game"],
                games.GetCurrentChoices()!.Select(choice => choice.Value));
            games.SetValueFromString("481");
            await refresh.InvokeAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("481", games.GetValueAsString());
            File.Delete(Path.Combine(secondary, "appmanifest_481.acf"));

            await refresh.InvokeAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("", games.GetValueAsString());
            Assert.Equal(["", "480"], games.GetCurrentChoices()!.Select(choice => choice.Key));
        }
        finally { directory.Delete(recursive: true); }

        static Task Manifest(string folder, string id, string name) => File.WriteAllTextAsync(
            Path.Combine(folder, $"appmanifest_{id}.acf"), $"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"{name}\" }}");
    }
}
