using Axorith.Sdk;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using BrowserApp = Axorith.Module.ApplicationLauncher.Apps.Browser.BrowserApp;
using DiscordApp = Axorith.Module.ApplicationLauncher.Apps.Discord.DiscordApp;
using JetBrainsApp = Axorith.Module.ApplicationLauncher.Apps.JetBrainsIDE.JetBrainsIDEApp;
using ObsApp = Axorith.Module.ApplicationLauncher.Apps.OBS.OBSApp;
using SpotifyApp = Axorith.Module.ApplicationLauncher.Apps.Spotify.SpotifyApp;
using SteamApp = Axorith.Module.ApplicationLauncher.Apps.Steam.SteamApp;
using VsCodeApp = Axorith.Module.ApplicationLauncher.Apps.VSCode.VSCodeApp;

namespace Axorith.Module.ApplicationLauncher;

public sealed class Module : LauncherAppBase, IModule
{
    private readonly Settings _settings;
    private readonly IReadOnlyDictionary<string, LauncherAppBase> _modules;
    private LauncherAppBase? _activeModule;

    public Module(
        IModuleLogger logger,
        IAppDiscoveryService appDiscovery,
        INotifier notifier,
        IHttpClientFactory httpClientFactory,
        ISecureStorageService secureStorage,
        IPlatformProcessService processService,
        IPlatformWindowService windowService)
        : base(logger, processService, windowService)
    {
        _modules = new Dictionary<string, LauncherAppBase>
        {
            ["Browser"] = new BrowserApp(logger, appDiscovery, processService, windowService),
            ["OBS"] = new ObsApp(logger, appDiscovery, notifier, processService, windowService),
            ["Discord"] = new DiscordApp(logger, appDiscovery, processService, windowService),
            ["VSCode"] = new VsCodeApp(logger, appDiscovery, processService, windowService),
            ["JetBrainsIDE"] = new JetBrainsApp(logger, appDiscovery, processService, windowService),
            ["Steam"] = new SteamApp(logger, notifier, appDiscovery, processService, windowService),
            ["Spotify"] = new SpotifyApp(logger, httpClientFactory, secureStorage, notifier, appDiscovery,
                processService, windowService)
        };
        _settings = new Settings(appDiscovery, _modules);
    }

    protected override LauncherSettingsBase Settings => _settings;

    protected override ProcessConfig BuildProcessConfig()
    {
        var config = base.BuildProcessConfig();
        var selected = _settings.ApplicationPath.GetCurrentValue();
        var custom = selected == Axorith.Module.ApplicationLauncher.Settings.CustomApp;
        return config with
        {
            ApplicationPath = custom ? _settings.CustomPath.GetCurrentValue() : selected,
            Arguments = custom ? _settings.ApplicationArgs.GetCurrentValue() : string.Empty,
            WorkingDirectory = _settings.UseCustomWorkingDirectory.GetCurrentValue()
                ? _settings.WorkingDirectory.GetCurrentValue()
                : null
        };
    }

    public override async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        var moduleKey = ApplicationSelector.GetLauncherModuleKey(_settings.ApplicationPath.GetCurrentValue());
        if (moduleKey == null || !_modules.TryGetValue(moduleKey, out _activeModule))
        {
            await base.OnSessionStartAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _settings.EnsureModuleInitializedAsync(moduleKey, cancellationToken).ConfigureAwait(false);
        _settings.SynchronizeModule(moduleKey);
        await _activeModule.OnSessionStartAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (_activeModule == null)
        {
            await base.OnSessionEndAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _activeModule.OnSessionEndAsync(cancellationToken).ConfigureAwait(false);
        _activeModule = null;
    }

    public override void Dispose()
    {
        foreach (var module in _modules.Values)
            module.Dispose();
        base.Dispose();
    }
}
