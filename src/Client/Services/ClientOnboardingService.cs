using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Core.Models;
using Axorith.Shared.Platform;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

public sealed class ClientOnboardingService(
    IAppDiscoveryService appDiscovery,
    IPresetsApi presetsApi,
    IModulesApi modulesApi,
    ILogger<ClientOnboardingService> logger)
{
    private const string CustomLauncherApp = "custom-app";
    private DiscoveredApps? _cachedApps;
    private DateTime _cacheTime;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    private static class BlockCategories
    {
        public const string CodingSiteCategories =
            "Social,Video,Streaming,Gaming,News,Shopping,Adult,Gambling,Dating,Forums";

        public const string CodingAppCategories = "Gaming,Social,Browsers,Entertainment";

        public const string GamingSiteCategories = "Work,News,Shopping";
        public const string GamingAppCategories = "Productivity,Email,Office,Development";

        public const string StreamingSiteCategories = "Social,News,Shopping,Adult,Gambling,Dating";
        public const string StreamingAppCategories = "Productivity,Email,Office,Development";

    }

    public async Task<OnboardingResult> RunSetupAsync(CancellationToken ct = default)
    {
        var result = new OnboardingResult();

        try
        {
            var modulesTask = modulesApi.ListModulesAsync(ct);
            var presetsTask = presetsApi.ListPresetsAsync(ct);
            var appsTask = Task.Run(() => ScanForKnownAppsParallel(), ct);

            await Task.WhenAll(modulesTask, presetsTask, appsTask);

            var availableModules = await modulesTask;
            var existingPresets = await presetsTask;
            var discoveredApps = await appsTask;

            var existingNames = existingPresets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var moduleIds = availableModules.ToDictionary(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase);

            logger.LogInformation(
                "App discovery - IDEs: VSCode={VSCode}, Rider={Rider}, CLion={CLion}, IntelliJ={IntelliJ}, PyCharm={PyCharm}",
                discoveredApps.VSCodePath != null, discoveredApps.RiderPath != null, discoveredApps.CLionPath != null,
                discoveredApps.IntelliJPath != null, discoveredApps.PyCharmPath != null);
            logger.LogInformation(
                "App discovery - Other: Steam={Steam}, OBS={OBS}, Discord={Discord}, Spotify={Spotify}",
                discoveredApps.SteamPath != null, discoveredApps.ObsPath != null, discoveredApps.DiscordPath != null,
                discoveredApps.SpotifyPath != null);

            var presetsToCreate = GenerateCodingPresets(discoveredApps, moduleIds);
            var gamingPreset = CreateGamingPreset(discoveredApps, moduleIds);
            if (gamingPreset.Modules.Count > 0) presetsToCreate.Add(gamingPreset);
            var streamingPreset = CreateStreamingPreset(discoveredApps, moduleIds);
            if (streamingPreset.Modules.Count > 0) presetsToCreate.Add(streamingPreset);

            foreach (var preset in presetsToCreate.Where(preset => preset.Modules.Count > 0))
            {
                preset.Name = GetUniqueName(preset.Name, existingNames);
                existingNames.Add(preset.Name);
                await CreatePresetWithRetryAsync(preset, existingNames, result, ct);
            }

            result.Success = result.CreatedPresets.Count > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Onboarding setup failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task CreatePresetWithRetryAsync(
        SessionPreset preset,
        HashSet<string> existingNames,
        OnboardingResult result,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await presetsApi.CreatePresetAsync(preset, ct);
                result.CreatedPresets.Add(preset.Name);
                logger.LogInformation("Created preset: {PresetName}", preset.Name);
                return;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists && attempt == 0)
            {
                preset.Name = GetUniqueName(preset.Name, existingNames);
                existingNames.Add(preset.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create preset {PresetName}", preset.Name);
                result.SkippedPresets.Add(preset.Name);
                result.Errors.Add($"Failed to create {preset.Name}: {ex.Message}");
                return;
            }
        }
    }

    private DiscoveredApps ScanForKnownAppsParallel()
    {
        if (_cachedApps != null && DateTime.UtcNow - _cacheTime < CacheExpiry)
        {
            return _cachedApps;
        }

        appDiscovery.GetInstalledApplicationsIndex();

        var apps = new DiscoveredApps();

        var scanTasks = new (string[] Names, Action<string?> Setter)[]
        {
            (["Code", "Code - Insiders"], p => apps.VSCodePath = p),
            (["rider64", "rider"], p => apps.RiderPath = p),
            (["clion64", "clion"], p => apps.CLionPath = p),
            (["idea64", "idea"], p => apps.IntelliJPath = p),
            (["webstorm64", "webstorm"], p => apps.WebStormPath = p),
            (["pycharm64", "pycharm"], p => apps.PyCharmPath = p),
            (["goland64", "goland"], p => apps.GoLandPath = p),
            (["phpstorm64", "phpstorm"], p => apps.PhpStormPath = p),
            (["rubymine64", "rubymine"], p => apps.RubyMinePath = p),
            (["datagrip64", "datagrip"], p => apps.DataGripPath = p),
            (["studio64", "studio"], p => apps.AndroidStudioPath = p),
            (["steam"], p => apps.SteamPath = p),
            (["Spotify"], p => apps.SpotifyPath = p),
            (["Discord"], p => apps.DiscordPath = p),
            (["obs64", "obs32", "obs"], p => apps.ObsPath = p),
            (["chrome"], p => apps.ChromePath = p),
            (["firefox"], p => apps.FirefoxPath = p),
            (["msedge"], p => apps.EdgePath = p),
            (["Streamlabs OBS", "Streamlabs"], p => apps.StreamlabsPath = p)
        };

        Parallel.ForEach(scanTasks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            task => task.Setter(appDiscovery.FindKnownApp(task.Names)));

        _cachedApps = apps;
        _cacheTime = DateTime.UtcNow;

        return apps;
    }

    private List<SessionPreset> GenerateCodingPresets(DiscoveredApps apps, Dictionary<string, Guid> moduleIds)
    {
        var presets = new List<SessionPreset>();

        var ideConfigs = new (string? Path, string PresetName, string ModuleName, string PathKey)[]
        {
            (apps.VSCodePath, "VS Code", "VS Code", "CodePath"),
            (apps.RiderPath, "Rider", "JetBrains IDE", "IDEPath"),
            (apps.CLionPath, "CLion", "JetBrains IDE", "IDEPath"),
            (apps.IntelliJPath, "IntelliJ", "JetBrains IDE", "IDEPath"),
            (apps.PyCharmPath, "PyCharm", "JetBrains IDE", "IDEPath"),
            (apps.WebStormPath, "WebStorm", "JetBrains IDE", "IDEPath"),
            (apps.GoLandPath, "GoLand", "JetBrains IDE", "IDEPath"),
            (apps.PhpStormPath, "PhpStorm", "JetBrains IDE", "IDEPath"),
            (apps.RubyMinePath, "RubyMine", "JetBrains IDE", "IDEPath"),
            (apps.AndroidStudioPath, "Android Studio", "JetBrains IDE", "IDEPath"),
            (apps.DataGripPath, "DataGrip", "JetBrains IDE", "IDEPath")
        };

        foreach (var (path, presetName, moduleName, pathKey) in ideConfigs)
        {
            if (path == null)
            {
                continue;
            }

            var preset = CreateCodingPreset(presetName, moduleName, pathKey, path, moduleIds, apps);
            if (preset.Modules.Count > 0)
            {
                presets.Add(preset);
            }
        }

        if (presets.Count == 0 && (moduleIds.ContainsKey("Site Blocker") || moduleIds.ContainsKey("App Blocker")))
        {
            var focusPreset = new SessionPreset { Id = Guid.NewGuid(), Name = "Focus Mode", Modules = [] };
            AddBlockers(focusPreset, moduleIds, BlockCategories.CodingSiteCategories, BlockCategories.CodingAppCategories,
                "Block Distracting Sites", "Block Distracting Apps");
            AddSpotifyModule(focusPreset, apps, moduleIds, "Focus Music");
            if (focusPreset.Modules.Count > 0) presets.Add(focusPreset);
        }

        return presets;
    }

    private SessionPreset CreateCodingPreset(
        string presetName,
        string moduleName,
        string pathKey,
        string executablePath,
        Dictionary<string, Guid> moduleIds,
        DiscoveredApps apps)
    {
        var preset = new SessionPreset { Id = Guid.NewGuid(), Name = presetName, Modules = [] };

        TryAddModule(preset, moduleIds, moduleName, $"Launch {Path.GetFileNameWithoutExtension(executablePath)}",
            settings: new Dictionary<string, string> { [pathKey] = executablePath });

        AddBlockers(preset, moduleIds, BlockCategories.CodingSiteCategories, BlockCategories.CodingAppCategories,
            "Block Distracting Sites", "Block Distracting Apps");
        AddSpotifyModule(preset, apps, moduleIds, "Focus Music");

        return preset;
    }

    private static void AddBlockers(SessionPreset preset, IReadOnlyDictionary<string, Guid> moduleIds,
        string siteCategories, string appCategories, string sitesName, string appsName)
    {
        TryAddModule(preset, moduleIds, "Site Blocker", sitesName, TimeSpan.FromSeconds(1),
            new Dictionary<string, string> { ["Categories"] = siteCategories.Replace(',', '|') });
        TryAddModule(preset, moduleIds, "App Blocker", appsName, TimeSpan.FromSeconds(1),
            new Dictionary<string, string> { ["Categories"] = appCategories.Replace(',', '|') });
    }

    private static void AddSpotifyModule(SessionPreset preset, DiscoveredApps apps,
        IReadOnlyDictionary<string, Guid> moduleIds, string customName)
    {
        if (apps.SpotifyPath is not { } path)
            return;

        TryAddModule(preset, moduleIds, "Spotify", customName, TimeSpan.FromSeconds(2),
            new Dictionary<string, string> { ["SpotifyPath"] = path });
    }

    private static bool TryAddModule(SessionPreset preset, IReadOnlyDictionary<string, Guid> moduleIds,
        string moduleName, string customName, TimeSpan startDelay = default,
        Dictionary<string, string>? settings = null)
    {
        if (!moduleIds.TryGetValue(moduleName, out var moduleId))
            return false;

        preset.Modules.Add(new ConfiguredModule
        {
            InstanceId = Guid.NewGuid(),
            ModuleId = moduleId,
            CustomName = customName,
            StartDelay = startDelay,
            Settings = settings ?? []
        });
        return true;
    }

    private SessionPreset CreateGamingPreset(DiscoveredApps apps, Dictionary<string, Guid> moduleIds)
    {
        var preset = new SessionPreset { Id = Guid.NewGuid(), Name = "Gaming", Modules = [] };

        if (apps.SteamPath is { } steamPath)
            TryAddModule(preset, moduleIds, "Steam", "Launch Steam",
                settings: new Dictionary<string, string> { ["SteamPath"] = steamPath });

        if (apps.DiscordPath is { } discordPath)
            TryAddModule(preset, moduleIds, "Discord", "Launch Discord", TimeSpan.FromSeconds(2),
                new Dictionary<string, string> { ["DiscordPath"] = discordPath });

        AddSpotifyModule(preset, apps, moduleIds, "Gaming Playlist");
        AddBlockers(preset, moduleIds, BlockCategories.GamingSiteCategories, BlockCategories.GamingAppCategories,
            "Block Work Sites", "Block Work Apps");

        return preset;
    }

    private SessionPreset CreateStreamingPreset(DiscoveredApps apps, Dictionary<string, Guid> moduleIds)
    {
        var preset = new SessionPreset { Id = Guid.NewGuid(), Name = "Streaming", Modules = [] };

        var hasStreamingSoftware = apps.ObsPath is { } obsPath &&
            TryAddModule(preset, moduleIds, "OBS Studio", "Launch OBS",
                settings: new Dictionary<string, string> { ["ObsPath"] = obsPath });

        if (apps.StreamlabsPath is { } streamlabsPath)
        {
            hasStreamingSoftware |= TryAddModule(preset, moduleIds, "Application Launcher", "Launch Streamlabs",
                settings: new Dictionary<string, string>
                {
                    ["ApplicationPath"] = CustomLauncherApp,
                    ["CustomPath"] = streamlabsPath
                });
        }

        if (!hasStreamingSoftware)
        {
            return preset;
        }

        var browserPath = apps.ChromePath ?? apps.FirefoxPath ?? apps.EdgePath;
        if (browserPath is not null)
            TryAddModule(preset, moduleIds, "Browser", "Open Stream Dashboard", TimeSpan.FromSeconds(3),
                new Dictionary<string, string>
                {
                    ["BrowserPath"] = browserPath,
                    ["StartUrl"] = "https://dashboard.twitch.tv"
                });

        if (apps.DiscordPath is { } discordPath)
            TryAddModule(preset, moduleIds, "Discord", "Launch Discord", TimeSpan.FromSeconds(2),
                new Dictionary<string, string> { ["DiscordPath"] = discordPath });

        AddSpotifyModule(preset, apps, moduleIds, "Stream Music");
        AddBlockers(preset, moduleIds, BlockCategories.StreamingSiteCategories, BlockCategories.StreamingAppCategories,
            "Block Distractions", "Block Work Apps");

        return preset;
    }

    private static string GetUniqueName(string baseName, HashSet<string> existingNames)
    {
        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        var counter = 1;
        string candidate;
        do
        {
            candidate = $"{baseName} ({counter})";
            counter++;
        } while (existingNames.Contains(candidate) && counter < 100);

        return candidate;
    }

    private sealed class DiscoveredApps
    {
        public string? VSCodePath;
        public string? RiderPath;
        public string? CLionPath;
        public string? IntelliJPath;
        public string? WebStormPath;
        public string? PyCharmPath;
        public string? GoLandPath;
        public string? PhpStormPath;
        public string? RubyMinePath;
        public string? DataGripPath;
        public string? AndroidStudioPath;
        public string? SteamPath;
        public string? SpotifyPath;
        public string? DiscordPath;
        public string? ObsPath;
        public string? ChromePath;
        public string? FirefoxPath;
        public string? EdgePath;
        public string? StreamlabsPath;
    }
}

public sealed class OnboardingResult
{
    public bool Success { get; set; }
    public List<string> CreatedPresets { get; } = [];
    public List<string> SkippedPresets { get; } = [];
    public List<string> Errors { get; } = [];
    public string? ErrorMessage { get; set; }
    public int CreatedCount => CreatedPresets.Count;
    public IReadOnlyList<string> CreatedPresetNames => CreatedPresets;
}
