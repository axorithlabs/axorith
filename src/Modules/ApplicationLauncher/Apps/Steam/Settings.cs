using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.ApplicationLauncher.Apps.Steam;

internal sealed class Settings : LauncherSettingsBase
{
    public override Setting<string> ApplicationPath => SteamPath;

    public Setting<string> SteamPath { get; }
    public Setting<string> SelectedGame { get; }

    public Action RefreshGamesAction { get; }

    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

        SteamPath = Setting.AsChoice(
            key: "SteamPath",
            label: "Steam Executable",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Scanning for Steam...")],
            description: "Path to Steam executable."
        );

        SelectedGame = Setting.AsChoice(
            key: "SelectedGame",
            label: "Game to Launch",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Select Steam path first...")],
            description: "Select a game to launch when session starts. Leave empty to just launch Steam."
        );

        RefreshGamesAction = new Action("RefreshGames", "Refresh Game List");
        RefreshGamesAction.OnInvokeAsync(RefreshGamesAsync);

        SetupBaseReactiveVisibility();
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings() => [SelectedGame];

    protected override IEnumerable<IAction> GetAdditionalActions() => [RefreshGamesAction];

    protected override async Task InitializeAdditionalAsync()
    {
        await RefreshSteamPathAsync();
        await RefreshGamesAsync();
    }

    private Task RefreshSteamPathAsync()
    {
        return Task.Run(() =>
        {
            var steamExe = _appDiscovery.FindKnownApp("steam.exe", "Steam");

            if (!string.IsNullOrEmpty(steamExe) && !File.Exists(steamExe))
                steamExe = null;

            SetDetectedApplicationPath(steamExe, "Steam (Auto-Detected)", "Steam not found",
                current => $"{Path.GetFileName(current)} (Custom)");
        });
    }

    private Task RefreshGamesAsync()
    {
        return Task.Run(() =>
        {
            var steamExe = SteamPath.GetCurrentValue();
            var steamDir = SteamGameScanner.GetSteamDirectory(steamExe);

            if (string.IsNullOrEmpty(steamDir))
            {
                SelectedGame.SetChoices([new KeyValuePair<string, string>("", "Steam not found")]);
                return;
            }

            var games = SteamGameScanner.GetInstalledGames(steamDir);

            var choices = new List<KeyValuePair<string, string>>
            {
                new("", "None (just launch Steam)")
            };
            choices.AddRange(games.Select(game => new KeyValuePair<string, string>(game.AppId, game.Name)));

            if (choices.Count == 1)
            {
                choices = [new KeyValuePair<string, string>("", "No games found")];
            }

            SelectedGame.SetChoices(choices);

            var current = SelectedGame.GetCurrentValue();
            if (!string.IsNullOrEmpty(current) && choices.All(c => c.Key != current))
            {
                SelectedGame.SetValue(string.Empty);
            }
        });
    }

}
