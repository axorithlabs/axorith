using System.Text;
using Axorith.Sdk.Logging;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;

namespace Axorith.Module.Browser;

/// <summary>
///     Module for launching web browsers with configurable options.
///     Supports profile selection, incognito mode, and custom URLs.
/// </summary>
public class Module(
    IModuleLogger logger,
    IAppDiscoveryService appDiscovery,
    IPlatformProcessService processService,
    IPlatformWindowService windowService) : LauncherModuleBase(logger, processService, windowService)
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
            var sanitizedProfileName = SanitizeArgument(profileName);
            args.Append(string.Format(profile.ProfileArgument, sanitizedProfileName));
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
            var sanitizedArgs = SanitizeAdditionalArguments(additionalArgs);
            args.Append(sanitizedArgs);
            args.Append(' ');
        }

        var startUrl = _settings.StartUrl.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(startUrl))
        {
            return args.ToString().Trim();
        }

        var sanitizedUrl = SanitizeUrl(startUrl);
        if (sanitizedUrl.Contains(' '))
        {
            args.Append($"\"{sanitizedUrl}\"");
        }
        else
        {
            args.Append(sanitizedUrl);
        }

        return args.ToString().Trim();
    }

    private static string SanitizeArgument(string argument)
    {
        return string.IsNullOrWhiteSpace(argument)
            ? string.Empty
            : argument.Replace("\"", "").Replace("'", "").Replace(";", "").Replace("&", "").Replace("|", "");
    }

    private static string SanitizeAdditionalArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        var dangerousFlags = new[]
        {
            "--gpu-launcher",
            "--renderer-cmd-prefix",
            "--utility-cmd-prefix",
            "--js-flags",
            "--enable-features=RunningInForcedAppMode",
            "--load-extension"
        };

        var lowerArgs = arguments.ToLowerInvariant();
        foreach (var flag in dangerousFlags)
        {
            if (lowerArgs.Contains(flag))
            {
                throw new InvalidOperationException(
                    $"Dangerous browser flag detected: {flag}. This flag is not allowed for security reasons.");
            }
        }

        return arguments;
    }

    private static string SanitizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid URL format: {url}");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"Only HTTP and HTTPS URLs are allowed. Got: {uri.Scheme}");
        }

        return uri.AbsoluteUri;
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