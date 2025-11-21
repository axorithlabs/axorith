using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

/// <summary>
///     The concrete implementation for managing session presets using JSON files on disk.
/// </summary>
public class PresetManager(string presetsDirectory, ILogger<PresetManager> logger) : IPresetManager
{
    private const int CurrentPresetVersion = 1;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public async Task<IReadOnlyList<SessionPreset>> LoadAllPresetsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading all presets from {Directory}", presetsDirectory);
        Directory.CreateDirectory(presetsDirectory);

        var presets = new List<SessionPreset>();
        var presetFiles = Directory.EnumerateFiles(presetsDirectory, "*.json");

        foreach (var filePath in presetFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Preset loading was cancelled");
                break;
            }

            try
            {
                await using var stream = File.OpenRead(filePath);
                var preset = JsonSerializer.Deserialize<SessionPreset>(stream, _jsonOptions);

                if (preset != null)
                {
                    if (preset.Version < CurrentPresetVersion)
                    {
                        logger.LogInformation(
                            "Migrating preset '{PresetName}' from version {OldVersion} to {NewVersion}",
                            preset.Name, preset.Version, CurrentPresetVersion);

                        MigratePreset(preset);
                        preset.Version = CurrentPresetVersion;

                        await SavePresetAsync(preset, cancellationToken).ConfigureAwait(false);
                    }

                    presets.Add(preset);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load or deserialize preset from {FilePath}", filePath);
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
            logger.LogWarning("Preset file not found: {FilePath}", filePath);
            return null;
        }

        try
        {
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load preset {PresetId} from {FilePath}", presetId, filePath);
            return null;
        }
    }

    public async Task SavePresetAsync(SessionPreset preset, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(presetsDirectory, $"{preset.Id}.json");
        var tempFilePath = Path.Combine(presetsDirectory, $"{preset.Id}.json.tmp");
        logger.LogInformation("Saving preset '{PresetName}' to {FilePath}", preset.Name, filePath);

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
        logger.LogInformation("Deleting preset with ID {PresetId} from {FilePath}", presetId, filePath);

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                logger.LogDebug("Preset file deleted successfully");
            }
            else
            {
                logger.LogWarning("Attempted to delete a preset that does not exist on disk: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete preset file {FilePath}", filePath);
        }

        return Task.CompletedTask;
    }

    private void MigratePreset(SessionPreset preset)
    {
        logger.LogDebug("Preset migration completed for '{PresetName}'", preset.Name);
    }
}