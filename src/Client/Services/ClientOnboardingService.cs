using Axorith.Client.CoreSdk.Abstractions;
using Axorith.Core.Models;
using Axorith.Shared.Platform;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

public interface IClientOnboardingService
{
    Task<OnboardingResult> RunSetupAsync(CancellationToken ct = default);
    Task<PresetDiscoveryResult> DiscoverAvailablePresetsAsync(CancellationToken ct = default);
    Task<OnboardingResult> CreateSelectedPresetsAsync(List<string> selectedTypes, CancellationToken ct = default);
}

public sealed class ClientOnboardingService : IClientOnboardingService
{
    private readonly IAppDiscoveryService _appDiscovery;
    private readonly IPresetsApi _presetsApi;
    private readonly IModulesApi _modulesApi;
    private readonly ILogger<ClientOnboardingService> _logger;
    private DiscoveredApps? _cachedApps;
    private DateTime _cacheTime;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    private static class BlockCategories
    {
        public const string CodingSiteCategories = "Social,Video,Streaming,Gaming,News,Shopping,Adult,Gambling,Dating,Forums";
        public const string CodingAppCategories = "Gaming,Social,Browsers,Entertainment";

        public const string GamingSiteCategories = "Work,News,Shopping";
        public const string GamingAppCategories = "Productivity,Email,Office,Development";

        public const string StreamingSiteCategories = "Social,News,Shopping,Adult,Gambling,Dating";
        public const string StreamingAppCategories = "Productivity,Email,Office,Development";

        public const string FocusSiteCategories = "Social,Video,Streaming,Gaming,News,Shopping,Adult,Gambling,Dating,Forums";
        public const string FocusAppCategories = "Gaming,Social,Browsers,Entertainment";
    }

    public ClientOnboardingService(
        IAppDiscoveryService appDiscovery,
        IPresetsApi presetsApi,
        IModulesApi modulesApi,
        ILogger<ClientOnboardingService> logger)
    {
        _appDiscovery = appDiscovery;
        _presetsApi = presetsApi;
        _modulesApi = modulesApi;
        _logger = logger;
    }

    public async Task<OnboardingResult> RunSetupAsync(CancellationToken ct = default)
    {
        var result = new OnboardingResult();

        try
        {
            var modulesTask = _modulesApi.ListModulesAsync(ct);
            var presetsTask = _presetsApi.ListPresetsAsync(ct);
            var appsTask = Task.Run(() => ScanForKnownAppsParallel(), ct);

            await Task.WhenAll(modulesTask, presetsTask, appsTask);

            var availableModules = await modulesTask;
            var existingPresets = await presetsTask;
            var discoveredApps = await appsTask;

            var existingNames = existingPresets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var moduleIds = availableModules.ToDictionary(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase);

            LogDiscoveredApps(discoveredApps);

            var presetsToCreate = GeneratePresets(discoveredApps, moduleIds);

            var createTasks = new List<Task>();
            foreach (var preset in presetsToCreate)
            {
                if (preset.Modules.Count == 0) continue;

                preset.Name = GetUniqueName(preset.Name, existingNames);
                existingNames.Add(preset.Name);

                createTasks.Add(CreatePresetWithRetryAsync(preset, existingNames, result, ct));
            }

            await Task.WhenAll(createTasks);

            result.Success = result.CreatedPresets.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Onboarding setup failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<PresetDiscoveryResult> DiscoverAvailablePresetsAsync(CancellationToken ct = default)
    {
        var result = new PresetDiscoveryResult();

        try
        {
            var modulesTask = _modulesApi.ListModulesAsync(ct);
            var appsTask = Task.Run(() => ScanForKnownAppsParallel(), ct);

            await Task.WhenAll(modulesTask, appsTask);

            var availableModules = await modulesTask;
            var discoveredApps = await appsTask;
            var moduleIds = availableModules.ToDictionary(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase);

            var codingPresets = GenerateCodingPresets(discoveredApps, moduleIds);
            result.CodingPresetCount = codingPresets.Count;
            result.HasCodingPresets = codingPresets.Count > 0;

            var gamingPreset = CreateGamingPreset(discoveredApps, moduleIds);
            result.GamingPresetCount = gamingPreset.Modules.Count > 0 ? 1 : 0;
            result.HasGamingPresets = gamingPreset.Modules.Count > 0;

            var streamingPreset = CreateStreamingPreset(discoveredApps, moduleIds);
            result.StreamingPresetCount = streamingPreset.Modules.Count > 0 ? 1 : 0;
            result.HasStreamingPresets = streamingPreset.Modules.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover available presets");
        }

        return result;
    }

    public async Task<OnboardingResult> CreateSelectedPresetsAsync(List<string> selectedTypes, CancellationToken ct = default)
    {
        var result = new OnboardingResult();

        try
        {
            var modulesTask = _modulesApi.ListModulesAsync(ct);
            var presetsTask = _presetsApi.ListPresetsAsync(ct);
            var appsTask = Task.Run(() => ScanForKnownAppsParallel(), ct);

            await Task.WhenAll(modulesTask, presetsTask, appsTask);

            var availableModules = await modulesTask;
            var existingPresets = await presetsTask;
            var discoveredApps = await appsTask;

            var existingNames = existingPresets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var moduleIds = availableModules.ToDictionary(m => m.Name, m => m.Id, StringComparer.OrdinalIgnoreCase);

            var presetsToCreate = new List<SessionPreset>();

            if (selectedTypes.Contains("Developer"))
            {
                presetsToCreate.AddRange(GenerateCodingPresets(discoveredApps, moduleIds));
            }

            if (selectedTypes.Contains("Gamer"))
            {
                var gamingPreset = CreateGamingPreset(discoveredApps, moduleIds);
                if (gamingPreset.Modules.Count > 0) presetsToCreate.Add(gamingPreset);
            }

            if (selectedTypes.Contains("Streamer"))
            {
                var streamingPreset = CreateStreamingPreset(discoveredApps, moduleIds);
                if (streamingPreset.Modules.Count > 0) presetsToCreate.Add(streamingPreset);
            }

            var createTasks = new List<Task>();
            foreach (var preset in presetsToCreate)
            {
                if (preset.Modules.Count == 0) continue;

                preset.Name = GetUniqueName(preset.Name, existingNames);
                existingNames.Add(preset.Name);

                createTasks.Add(CreatePresetWithRetryAsync(preset, existingNames, result, ct));
            }

            await Task.WhenAll(createTasks);

            result.Success = result.CreatedPresets.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create selected presets");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private void LogDiscoveredApps(DiscoveredApps apps)
    {
        _logger.LogInformation(
            "App discovery - IDEs: VSCode={VSCode}, Rider={Rider}, CLion={CLion}, IntelliJ={IntelliJ}, PyCharm={PyCharm}",
            apps.VSCodePath != null, apps.RiderPath != null, apps.CLionPath != null,
            apps.IntelliJPath != null, apps.PyCharmPath != null);

        _logger.LogInformation(
            "App discovery - Other: Steam={Steam}, OBS={OBS}, Discord={Discord}, Spotify={Spotify}",
            apps.SteamPath != null, apps.ObsPath != null, apps.DiscordPath != null, apps.SpotifyPath != null);
    }

    private async Task CreatePresetWithRetryAsync(
        SessionPreset preset,
        HashSet<string> existingNames,
        OnboardingResult result,
        CancellationToken ct)
    {
        try
        {
            await _presetsApi.CreatePresetAsync(preset, ct);
            lock (result) result.CreatedPresets.Add(preset.Name);
            _logger.LogInformation("Created preset: {PresetName}", preset.Name);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            string retryName;
            lock (existingNames)
            {
                retryName = GetUniqueName(preset.Name, existingNames);
                preset.Name = retryName;
                existingNames.Add(retryName);
            }

            try
            {
                await _presetsApi.CreatePresetAsync(preset, ct);
                lock (result) result.CreatedPresets.Add(preset.Name);
                _logger.LogInformation("Created preset with retry name: {PresetName}", preset.Name);
            }
            catch (Exception retryEx)
            {
                _logger.LogWarning(retryEx, "Failed to create preset {PresetName} after retry", preset.Name);
                lock (result)
                {
                    result.SkippedPresets.Add(preset.Name);
                    result.Errors.Add($"Failed to create {preset.Name}: {retryEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create preset {PresetName}", preset.Name);
            lock (result)
            {
                result.SkippedPresets.Add(preset.Name);
                result.Errors.Add($"Failed to create {preset.Name}: {ex.Message}");
            }
        }
    }

    private DiscoveredApps ScanForKnownAppsParallel()
    {
        if (_cachedApps != null && DateTime.UtcNow - _cacheTime < CacheExpiry)
        {
            return _cachedApps;
        }

        _appDiscovery.GetInstalledApplicationsIndex();

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
            (["slack"], p => apps.SlackPath = p),
            (["obs64", "obs32", "obs"], p => apps.ObsPath = p),
            (["chrome"], p => apps.ChromePath = p),
            (["firefox"], p => apps.FirefoxPath = p),
            (["msedge"], p => apps.EdgePath = p),
            (["Streamlabs OBS", "Streamlabs"], p => apps.StreamlabsPath = p)
        };

        Parallel.ForEach(scanTasks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            task => task.Setter(_appDiscovery.FindKnownApp(task.Names)));

        _cachedApps = apps;
        _cacheTime = DateTime.UtcNow;

        return apps;
    }

    private List<SessionPreset> GeneratePresets(DiscoveredApps apps, Dictionary<string, Guid> moduleIds)
    {
        var presets = new List<SessionPreset>();

        presets.AddRange(GenerateCodingPresets(apps, moduleIds));

        var gamingPreset = CreateGamingPreset(apps, moduleIds);
        if (gamingPreset.Modules.Count > 0) presets.Add(gamingPreset);

        var streamingPreset = CreateStreamingPreset(apps, moduleIds);
        if (streamingPreset.Modules.Count > 0) presets.Add(streamingPreset);

        return presets;
    }

    private List<SessionPreset> GenerateCodingPresets(DiscoveredApps apps, Dictionary<string, Guid> moduleIds)
    {
        var presets = new List<SessionPreset>();

        var ideConfigs = new (string? Path, string PresetName, string ModuleName)[]
        {
            (apps.VSCodePath, "VS Code", "VS Code"),
            (apps.RiderPath, "Rider", "JetBrains IDE"),
            (apps.CLionPath, "CLion", "JetBrains IDE"),
            (apps.IntelliJPath, "IntelliJ", "JetBrains IDE"),
            (apps.PyCharmPath, "PyCharm", "JetBrains IDE"),
            (apps.WebStormPath, "WebStorm", "JetBrains IDE"),
            (apps.GoLandPath, "GoLand", "JetBrains IDE"),
            (apps.PhpStormPath, "PhpStorm", "JetBrains IDE"),
            (apps.RubyMinePath, "RubyMine", "JetBrains IDE"),
            (apps.AndroidStudioPath, "Android Studio", "JetBrains IDE"),
            (apps.DataGripPath, "DataGrip", "JetBrains IDE")
        };

        foreach (var (path, presetName, moduleName) in ideConfigs)
        {
            if (path == null) continue;
            var preset = CreateCodingPreset(presetName, moduleName, path, moduleIds, apps);
            if (preset.Modules.Count > 0) presets.Add(preset);
        }

        if (presets.Count == 0 && (moduleIds.ContainsKey("Site Blocker") || moduleIds.ContainsKey("App Blocker")))
        {
            var focusPreset = CreateFocusModePreset(moduleIds, apps);
            if (focusPreset.Modules.Count > 0) presets.Add(focusPreset);
        }

        return presets;
    }

    private SessionPreset CreateCodingPreset(
        string presetName,
        string moduleName,
        string executablePath,
        Dictionary<string, Guid> moduleIds,
        DiscoveredApps apps)
    {
        var preset = new SessionPreset { Id = Guid.NewGuid(), Name = presetName, Modules = [] };

        if (moduleIds.TryGetValue(moduleName, out var moduleId))
        {
            preset.Modules.Add(new ConfiguredModule
            {
                InstanceId = Guid.NewGuid(),
                ModuleId = moduleId,
                CustomName = $"Launch {Path.GetFileNameWithoutExtension(executablePath)}",
                Settings = new Dictionary<string, string> { ["executablePath"] = executablePath }
            });
        }

        AddBlockers(preset, moduleIds, BlockCategories.CodingSiteCategories, BlockCategories.CodingAppCategories,
            "Block Distracting Sites", "Block Distracting Apps");
        AddSpotifyModule(preset, apps, moduleIds, "Focus Music");

        return preset;
    }

    private SessionPreset CreateFocusModePreset(Dictionary<string, Guid> moduleIds, DiscoveredApps apps)
    {
        var preset = new SessionPreset { Id = Guid.NewGuid(), Name = "Focus Mode", Modules = [] };
        AddBlockers(preset, moduleIds, BlockCategories.FocusSiteCategories, BlockCategories.FocusAppCategories,
            "Block Distracting Sites", "Block Distracting Apps");
        AddSpotifyModule(preset, apps, moduleIds, "Focus Music");
        return preset;
    }

    private static void AddBlockers(SessionPreset preset, Dictionary<string, Guid> moduleIds,
        string siteCategories, string appCategories, string sitesName, string appsName)
    {
        if (moduleIds.TryGetValue("Site Blocker", out var siteBlockerId))
        {
            preset.Modules.Add(new ConfiguredModule
            {
                InstanceId = Guid.NewGuid(),
                ModuleId = siteBlockerId,
                CustomName = sitesName,
                StartDelay = TimeSpan.FromSeconds(1),
                Settings = new Dictionary<string, string> { ["Categories"] = siteCategories.Replace(",", "|") }
            });
        }

        if (moduleIds.TryGetValue("App Blocker", out var appBlockerId))
        {
            preset.Modules.Add(new ConfiguredModule
            {
                InstanceId = Guid.NewGuid(),
                ModuleId = appBlockerId,
                CustomName = appsName,
                StartDelay = TimeSpan.FromSeconds(1),
                Settings = new Dictionary<string, string> { ["Categories"] = appCategories.Replace(",", "|") }
            });
        }
    }

    private static void AddSpotifyModule(SessionPreset preset, DiscoveredApps apps, Dictionary<string, Guid> moduleIds, string customName)
    {
        if (apps.SpotifyPath == null || !moduleIds.TryGetValue("Spotify", out var spotifyId)) return;

        preset.Modules.Add(new ConfiguredModule
        {
            InstanceId = Guid.NewGuid(),
            ModuleId = spotifyId,
            CustomName = customName,
            StartDelay = TimeSpan.FromSeconds(2)
        });
    }

    private SessionPreset CreateGamingPreset(DiscoveredApps apps, Dictionary<string, Guid> moduleIds)
    {
        var preset = new SessionPreset { Id = Guid.NewGuid(), Name = "Gaming", Modules = [] };

        if (apps.SteamPath != null && moduleIds.TryGetValue("Steam", out var steamId))
        {
            preset.Modules.Add(new ConfiguredModule
            {
                InstanceId = Guid.NewGuid(),
                ModuleId = steamId,
                CustomName = "Launch Steam",
                Settings = new Dictionary<string, string> { ["executablePath"] = apps.SteamPath }
            });
        }

        if (apps.DiscordPath != null && moduleIds.TryGetValue("Discord", out var discordId))
        {
            preset.Modules.Add(new ConfiguredModule
            {
                InstanceId = Guid.NewGuid(),
                ModuleId = discordId,
                CustomName = "Launch Discord",
                StartDelay = TimeSpan.FromSeconds(2)
            });
        }

        AddSpotifyModule(preset, apps, moduleIds, "Gaming Playlist");
        AddBlockers(preset, moduleIds, BlockCategories.GamingSiteCategories, BlockCategories.GamingAppCategories,
            "Block Work Sites", "Block Work Apps");

        return preset;
    }

    private SessionPreset CreateStreamingPreset(DiscoveredApps apps, Dictionary<string, Guid> moduleIds)
    {
        var preset = new SessionPreset { Id = Guid.NewGuid(), Name = "Streaming", Modules = [] };

        var hasStreamingSoftware = false;

        if (apps.ObsPath != null && moduleIds.TryGetValue("OBS Studio", out var obsId))
        {
            preset.Modules.Add(new ConfiguredModule
            {
                InstanceId = Guid.NewGuid(),
                ModuleId = obsId,
                CustomName = "Launch OBS",
                Settings = new Dictionary<string, string> { ["executablePath"] = apps.ObsPath }
            });
            hasStreamingSoftware = true;
        }

        if (apps.StreamlabsPath != null && moduleIds.TryGetValue("Application Launcher", out var launcherId))
        {
            preset.Modules.Add(new ConfiguredModule
            {
                InstanceId = Guid.NewGuid(),
                ModuleId = launcherId,
                CustomName = "Launch Streamlabs",
                Settings = new Dictionary<string, string> { ["executablePath"] = apps.StreamlabsPath }
            });
            hasStreamingSoftware = true;
        }

        if (!hasStreamingSoftware) return preset;

        var browserPath = apps.ChromePath ?? apps.FirefoxPath ?? apps.EdgePath;
        if (browserPath != null && moduleIds.TryGetValue("Browser", out var browserId))
        {
            preset.Modules.Add(new ConfiguredModule
            {
                InstanceId = Guid.NewGuid(),
                ModuleId = browserId,
                CustomName = "Open Stream Dashboard",
                StartDelay = TimeSpan.FromSeconds(3),
                Settings = new Dictionary<string, string> { ["executablePath"] = browserPath, ["url"] = "https://dashboard.twitch.tv" }
            });
        }

        if (apps.DiscordPath != null && moduleIds.TryGetValue("Discord", out var discordId))
        {
            preset.Modules.Add(new ConfiguredModule
            {
                InstanceId = Guid.NewGuid(),
                ModuleId = discordId,
                CustomName = "Launch Discord",
                StartDelay = TimeSpan.FromSeconds(2)
            });
        }

        AddSpotifyModule(preset, apps, moduleIds, "Stream Music");
        AddBlockers(preset, moduleIds, BlockCategories.StreamingSiteCategories, BlockCategories.StreamingAppCategories,
            "Block Distractions", "Block Work Apps");

        return preset;
    }

    private static string GetUniqueName(string baseName, HashSet<string> existingNames)
    {
        if (!existingNames.Contains(baseName)) return baseName;

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
        public string? SlackPath;
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

public sealed class PresetDiscoveryResult
{
    public bool HasCodingPresets { get; set; }
    public int CodingPresetCount { get; set; }
    public bool HasGamingPresets { get; set; }
    public int GamingPresetCount { get; set; }
    public bool HasStreamingPresets { get; set; }
    public int StreamingPresetCount { get; set; }
}
