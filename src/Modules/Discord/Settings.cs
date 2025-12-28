using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.Discord;

internal sealed class Settings : LauncherSettingsBase
{
    public override Setting<string> ApplicationPath => DiscordPath;

    public Setting<string> DiscordPath { get; }
    public Action RefreshPathAction { get; }

    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

        DiscordPath = Setting.AsChoice(
            key: "DiscordPath",
            label: "Discord Executable",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Scanning for Discord...")],
            description: "Path to Discord executable."
        );

        RefreshPathAction = Action.Create("RefreshPath", "Refresh Path");
        RefreshPathAction.OnInvokeAsync(RefreshPathAsync);

        SetupBaseReactiveVisibility();
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings()
    {
        yield break;
    }

    protected override IEnumerable<IAction> GetAdditionalActions()
    {
        yield return RefreshPathAction;
    }

    protected override async Task InitializeAdditionalAsync()
    {
        await RefreshPathAsync();
    }

    private Task RefreshPathAsync()
    {
        return Task.Run(() =>
        {
            var platform = EnvironmentUtils.GetCurrentPlatform();
            string? path = null;

            if (platform == Platform.Windows)
            {
                var discordBase = Path.Combine(ApplicationPaths.LocalRoot, "Discord");

                if (Directory.Exists(discordBase))
                {
                    var appFolders = Directory.GetDirectories(discordBase, "app-*")
                        .OrderByDescending(d => d)
                        .ToList();

                    foreach (var appFolder in appFolders)
                    {
                        var discordExe = Path.Combine(appFolder, "Discord.exe");
                        if (File.Exists(discordExe))
                        {
                            path = discordExe;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(path))
                {
                    path = _appDiscovery.FindKnownApp("Discord.exe", "Discord");
                }
            }
            else
            {
                path = _appDiscovery.FindKnownApp("discord");
            }

            var choices = new List<KeyValuePair<string, string>>
            {
                !string.IsNullOrEmpty(path)
                    ? new KeyValuePair<string, string>(path, "Discord (Auto-Detected)")
                    : new KeyValuePair<string, string>("", "Discord not found")
            };

            var current = DiscordPath.GetCurrentValue();
            if (!string.IsNullOrEmpty(current) && choices.All(c => c.Key != current))
            {
                choices.Insert(0, new KeyValuePair<string, string>(current, $"{current} (Custom)"));
            }

            DiscordPath.SetChoices(choices);

            if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(path))
            {
                DiscordPath.SetValue(path);
            }
        });
    }

    public override void Dispose()
    {
        DiscordPath.Dispose();
        RefreshPathAction.Dispose();
        base.Dispose();
    }
}
