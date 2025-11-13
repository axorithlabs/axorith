using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

/// <summary>
///     The concrete implementation for managing session presets using JSON files on disk.
/// </summary>
/// <remarks>
///     Initializes a new instance of the <see cref="PresetManager" /> class.
/// </remarks>
/// <param name="presetsDirectory">The resolved presets directory path from configuration.</param>
/// <param name="logger">The logger instance.</param>
public class PresetManager(string presetsDirectory, ILogger<PresetManager> logger) : IPresetManager
{
    private const int CurrentPresetVersion = 1;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

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
                    // Migrate preset if needed
                    if (preset.Version < CurrentPresetVersion)
                    {
                        logger.LogInformation(
                            "Migrating preset '{PresetName}' from version {OldVersion} to {NewVersion}",
                            preset.Name, preset.Version, CurrentPresetVersion);

                        MigratePreset(preset);
                        preset.Version = CurrentPresetVersion;

                        // Save migrated preset
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

            if (preset is not { Version: < CurrentPresetVersion }) return preset;

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

        try
        {
            Directory.CreateDirectory(presetsDirectory);

            // Write to temporary file first with exclusive access to prevent concurrent writes
            // FileShare.None ensures no other process can access this file while we're writing
            await using (var stream = new FileStream(
                             tempFilePath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None, // Exclusive access - no other process can read/write
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, preset, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Atomic rename: if this succeeds, the file is guaranteed to be complete
            // This operation is atomic on Windows and POSIX systems
            File.Move(tempFilePath, filePath, overwrite: true);
            logger.LogDebug("Preset '{PresetName}' saved successfully", preset.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save preset '{PresetName}'", preset.Name);

            // Clean up temporary file if it exists
            try
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
            catch
            {
                // Ignore cleanup errors
            }
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

    /// <summary>
    ///     Migrates a preset from an older version to the current version.
    ///     Add migration logic here when preset structure changes.
    /// </summary>
    private void MigratePreset(SessionPreset preset)
    {
        // Example migration logic (add more as needed when schema changes):
        // if (preset.Version == 0)
        // {
        //     // Migrate from version 0 to 1
        //     // e.g., rename settings keys, convert data formats, etc.
        // }

        logger.LogDebug("Preset migration completed for '{PresetName}'", preset.Name);
    }
}