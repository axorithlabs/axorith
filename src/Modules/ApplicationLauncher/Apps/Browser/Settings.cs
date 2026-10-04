using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.ApplicationLauncher.Apps.Browser;

internal sealed class Settings : LauncherSettingsBase
{
    private static readonly string[] BrowserPublishers =
    [
        "Google", "Mozilla", "Microsoft", "Brave Software", "Opera Software",
        "Vivaldi Technologies", "The Chromium Authors", "Waterfox Limited",
        "LibreWolf", "Ablaze Floorp", "Zen Browser", "Moonchild Productions",
        "Yandex", "The Browser Company", "Tor Project"
    ];

    public override Setting<string> ApplicationPath => BrowserPath;

    public Setting<string> BrowserPath { get; }
    public Setting<string> StartUrl { get; }
    public Setting<string> ProfileName { get; }
    public Setting<bool> IncognitoMode { get; }
    public Setting<string> AdditionalArgs { get; }

    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

        BrowserPath = Setting.AsChoice(
            key: "BrowserPath",
            label: "Browser",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Scanning for browsers...")],
            description: "Select the browser to launch."
        );

        StartUrl = Setting.AsText(
            key: "StartUrl",
            label: "Start URL",
            defaultValue: string.Empty,
            description: "Optional URL to open when browser starts."
        );

        ProfileName = Setting.AsText(
            key: "ProfileName",
            label: "Profile Name",
            defaultValue: string.Empty,
            description: "Optional browser profile name (Chrome: 'Profile 1', Firefox: profile name)."
        );

        IncognitoMode = Setting.AsCheckbox(
            key: "IncognitoMode",
            label: "Private/Incognito Mode",
            defaultValue: false,
            description: "Launch browser in private/incognito mode."
        );

        AdditionalArgs = Setting.AsText(
            key: "AdditionalArgs",
            label: "Additional Arguments",
            defaultValue: string.Empty,
            description: "Additional command-line arguments to pass to the browser."
        );

        SetupBaseReactiveVisibility();
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings() => [StartUrl, ProfileName, IncognitoMode, AdditionalArgs];

    protected override Task InitializeAdditionalAsync() => RefreshBrowsersAsync();

    protected override Task<ValidationResult> ValidateAdditionalAsync()
    {
        var url = StartUrl.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(ValidationResult.Success);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return Task.FromResult(ValidationResult.Fail(
                new Dictionary<string, string>
                    { [StartUrl.Key] = "Please enter a valid URL (e.g., https://example.com)." },
                "Invalid URL format."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    public BrowserProfile? GetSelectedBrowserProfile()
    {
        var path = BrowserPath.GetCurrentValue();
        return string.IsNullOrEmpty(path) ? null : BrowserProfiles.GetProfile(path);
    }

    private Task RefreshBrowsersAsync()
    {
        return Task.Run(() =>
        {
            var browsers = new List<AppInfo>();

            foreach (var publisher in BrowserPublishers)
            {
                try
                {
                    var publisherApps = _appDiscovery.FindAppsByPublisher(publisher);
                    browsers.AddRange(publisherApps.Where(ApplicationSelector.IsBrowserApp));
                }
                catch
                {
                    // Continue with other publishers
                }
            }

            var allApps = _appDiscovery.GetInstalledApplicationsIndex();
            var additionalBrowsers = allApps.Where(ApplicationSelector.IsBrowserApp);
            browsers.AddRange(additionalBrowsers);

            var uniqueBrowsers = browsers
                .GroupBy(app => Path.GetFileName(app.ExecutablePath), StringComparer.OrdinalIgnoreCase)
                .Select(PickBestBrowserEntry)
                .OrderBy(app => app.Name)
                .ToList();

            var choices = uniqueBrowsers
                .Select(b => new KeyValuePair<string, string>(b.ExecutablePath, b.Name))
                .ToList();

            if (choices.Count == 0)
            {
                choices.Add(new KeyValuePair<string, string>("", "No browsers found"));
            }

            var current = BrowserPath.GetCurrentValue();
            if (!string.IsNullOrEmpty(current) &&
                choices.All(c => !c.Key.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                choices.Insert(0, new KeyValuePair<string, string>(current, $"{Path.GetFileName(current)} (Custom)"));
            }

            BrowserPath.SetChoices(choices);

            if (string.IsNullOrEmpty(current) && choices.Count > 0 && !string.IsNullOrEmpty(choices[0].Key))
            {
                BrowserPath.SetValue(choices[0].Key);
            }
        });
    }

    private static AppInfo PickBestBrowserEntry(IGrouping<string, AppInfo> group)
    {
        var entries = group.ToList();
        if (entries.Count == 1)
        {
            return entries[0];
        }

        var exeNameWithoutExt = Path.GetFileNameWithoutExtension(group.Key);

        return entries
            .OrderByDescending(e => !e.Name.Equals(exeNameWithoutExt, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(e => e.Name.Contains(' '))
            .ThenBy(e => e.Name.Length)
            .First();
    }

}
