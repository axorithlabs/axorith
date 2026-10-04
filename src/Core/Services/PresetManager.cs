using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

public class PresetManager(string presetsDirectory, ILogger<PresetManager> logger) : IPresetManager
{
    private const int CurrentPresetVersion = 3;
    private static readonly Guid ApplicationLauncherId = Guid.Parse("9b65a0b6-ce3e-4085-9ffa-b47c8fefcffd");
    private static readonly IReadOnlyDictionary<Guid, string> LegacyLauncherPathKeys = new Dictionary<Guid, string>
    {
        [Guid.Parse("6072c5d0-68eb-483c-b2c5-d068eb783c9e")] = "BrowserPath",
        [Guid.Parse("4f083ec9-518f-460a-883e-c9518fc60a28")] = "ObsPath",
        [Guid.Parse("30741589-7dba-42a2-b415-897dba32a2ef")] = "DiscordPath",
        [Guid.Parse("6b3271d3-3eae-41f4-b271-d33eaea1f40a")] = "CodePath",
        [Guid.Parse("c5f5e7b2-9d2b-4e1a-9f4b-3f4b7e8c5a21")] = "IdePath",
        [Guid.Parse("f4996a05-373d-4fa4-996a-05373d8fa4ea")] = "SteamPath",
        [Guid.Parse("04399d2f-43c9-4182-b99d-2f43c97182a6")] = "SpotifyPath"
    };
    private const long MaxPresetFileSizeBytes = 10 * 1024 * 1024; // 10 MB max

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 64 // Prevent stack overflow from deeply nested JSON
    };

    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public async Task<IReadOnlyList<SessionPreset>> LoadAllPresetsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading all presets from {Directory}", TelemetryGuard.SafePath(presetsDirectory));
        Directory.CreateDirectory(presetsDirectory);

        var presets = new List<SessionPreset>();
        foreach (var filePath in Directory.EnumerateFiles(presetsDirectory, "*.json"))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Preset loading was cancelled");
                break;
            }

            try
            {
                var preset = await LoadPresetFileAsync(filePath, cancellationToken).ConfigureAwait(false);
                if (preset != null)
                {
                    presets.Add(preset);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load or deserialize preset from {FilePath}",
                    TelemetryGuard.SafePath(filePath));
            }
        }

        logger.LogInformation("Successfully loaded {Count} presets", presets.Count);
        return presets;
    }

    public async Task<SessionPreset?> GetPresetByIdAsync(Guid presetId, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(presetsDirectory, $"{presetId}.json");
        if (!File.Exists(filePath))
        {
            logger.LogWarning("Preset file not found: {FilePath}", TelemetryGuard.SafePath(filePath));
            return null;
        }

        try
        {
            return await LoadPresetFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load preset {PresetId} from {FilePath}", presetId,
                TelemetryGuard.SafePath(filePath));
            return null;
        }
    }

    public async Task SavePresetAsync(SessionPreset preset, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(presetsDirectory, $"{preset.Id}.json");
        var tempFilePath = Path.Combine(presetsDirectory, $"{preset.Id}.json.tmp");
        logger.LogInformation("Saving preset '{PresetName}' to {FilePath}", preset.Name,
            TelemetryGuard.SafePath(filePath));

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(presetsDirectory);

            await using (var stream = new FileStream(
                             tempFilePath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, preset, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempFilePath, filePath, overwrite: true);

            logger.LogDebug("Preset '{PresetName}' saved successfully", preset.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save preset '{PresetName}'", preset.Name);

            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        finally
        {
            FileLock.Release();
        }
    }

    public Task DeletePresetAsync(Guid presetId, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(presetsDirectory, $"{presetId}.json");
        logger.LogInformation("Deleting preset with ID {PresetId} from {FilePath}", presetId,
            TelemetryGuard.SafePath(filePath));

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                logger.LogDebug("Preset file deleted successfully");
            }
            else
            {
                logger.LogWarning("Attempted to delete a preset that does not exist on disk: {FilePath}",
                    TelemetryGuard.SafePath(filePath));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete preset file {FilePath}", TelemetryGuard.SafePath(filePath));
        }

        return Task.CompletedTask;
    }

    private async Task<SessionPreset?> LoadPresetFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxPresetFileSizeBytes)
        {
            logger.LogWarning("Preset file {FilePath} exceeds maximum size limit ({Size} bytes)",
                TelemetryGuard.SafePath(filePath), fileInfo.Length);
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        var preset = await JsonSerializer.DeserializeAsync<SessionPreset>(stream, _jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (preset is not { Version: < CurrentPresetVersion })
        {
            return preset;
        }

        logger.LogInformation("Migrating preset '{PresetName}' from version {OldVersion} to {NewVersion}",
            preset.Name, preset.Version, CurrentPresetVersion);
        MigratePreset(preset);
        preset.Version = CurrentPresetVersion;
        await SavePresetAsync(preset, cancellationToken).ConfigureAwait(false);
        return preset;
    }

    private void MigratePreset(SessionPreset preset)
    {
        foreach (var module in preset.Modules)
        {
            if (!LegacyLauncherPathKeys.TryGetValue(module.ModuleId, out var pathKey))
                continue;

            if (module.Settings.TryGetValue(pathKey, out var appPath))
            {
                module.Settings.Remove(pathKey);
                module.Settings["ApplicationPath"] = appPath;
            }

            module.ModuleId = ApplicationLauncherId;
        }

        logger.LogDebug("Preset migration completed for '{PresetName}'", preset.Name);
    }
}
