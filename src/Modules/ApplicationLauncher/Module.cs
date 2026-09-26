using Axorith.Sdk;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using System.Net.Http;
using BrowserModule = Axorith.Module.Browser.Module;
using DiscordModule = Axorith.Module.Discord.Module;
using JetBrainsModule = Axorith.Module.JetBrainsIDE.Module;
using ObsModule = Axorith.Module.OBS.Module;
using SpotifyModule = Axorith.Module.Spotify.Module;
using SteamModule = Axorith.Module.Steam.Module;
using VsCodeModule = Axorith.Module.VSCode.Module;

namespace Axorith.Module.ApplicationLauncher;

public sealed class Module : LauncherModuleBase
{
    private static readonly ModuleDefinition SpotifyDefinition = new()
    {
        Id = Guid.Parse("04399d2f-43c9-4182-b99d-2f43c97182a6"),
        Name = "Spotify",
        Description = "Spotify playback submodule",
        Category = "App",
        Platforms = [Axorith.Sdk.Platform.Windows]
    };

    private readonly Settings _settings;
    private readonly IReadOnlyDictionary<string, IModule> _modules;
    private IModule? _activeModule;

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
        _modules = new Dictionary<string, IModule>
        {
            ["Browser"] = new BrowserModule(logger, appDiscovery, processService, windowService),
            ["OBS"] = new ObsModule(logger, appDiscovery, notifier, processService, windowService),
            ["Discord"] = new DiscordModule(logger, appDiscovery, processService, windowService),
            ["VSCode"] = new VsCodeModule(logger, appDiscovery, processService, windowService),
            ["JetBrainsIDE"] = new JetBrainsModule(logger, appDiscovery, processService, windowService),
            ["Steam"] = new SteamModule(logger, notifier, appDiscovery, processService, windowService),
            ["Spotify"] = new SpotifyModule(logger, httpClientFactory, secureStorage, notifier, appDiscovery,
                processService, windowService, SpotifyDefinition)
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
