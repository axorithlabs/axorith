using System.Text;
using Axorith.Sdk.Logging;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.Browser;

/// <summary>
///     Module for launching web browsers with configurable options.
///     Supports profile selection, incognito mode, and custom URLs.
/// </summary>
public class Module(IModuleLogger logger, IAppDiscoveryService appDiscovery) : LauncherModuleBase(logger)
{
    private readonly Settings _settings = new(appDiscovery);

    /// <inheritdoc />
    protected override LauncherSettingsBase Settings => _settings;

    /// <inheritdoc />
    protected override string GetLaunchArguments()
    {
        var args = new StringBuilder();
        var profile = _settings.GetSelectedBrowserProfile();

        var profileName = _settings.ProfileName.GetCurrentValue();
        if (!string.IsNullOrWhiteSpace(profileName) && profile?.ProfileArgument != null)
        {
            args.Append(string.Format(profile.ProfileArgument, profileName));
            args.Append(' ');
        }

        if (_settings.IncognitoMode.GetCurrentValue() && profile?.IncognitoArgument != null)
        {
            args.Append(profile.IncognitoArgument);
            args.Append(' ');
        }

        var additionalArgs = _settings.AdditionalArgs.GetCurrentValue();
        if (!string.IsNullOrWhiteSpace(additionalArgs))
        {
            args.Append(additionalArgs);
            args.Append(' ');
        }

        var startUrl = _settings.StartUrl.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(startUrl))
        {
            return args.ToString().Trim();
        }

        if (startUrl.Contains(' '))
        {
            args.Append($"\"{startUrl}\"");
        }
        else
        {
            args.Append(startUrl);
        }

        return args.ToString().Trim();
    }

    /// <inheritdoc />
    protected override WindowConfigTimings GetWindowConfigTimings()
    {
        return new WindowConfigTimings(
            WaitForWindowTimeoutMs: 10000,
            MoveDelayMs: 500,
            MaximizeSnapDelayMs: 500,
            FinalFocusDelayMs: 500,
            BannerDelayMs: 0
        );
    }
}