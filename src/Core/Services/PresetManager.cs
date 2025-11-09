using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

/// <summary>
///     The concrete implementation for managing session presets using JSON files on disk.
/// </summary>
public class PresetManager : IPresetManager
{
    private const int CurrentPresetVersion = 1;
    private readonly string _presetsDirectory;
    private readonly ILogger<PresetManager> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    /// <summary>
    ///     Initializes a new instance of the <see cref="PresetManager"/> class.
    /// </summary>
    /// <param name="presetsDirectory">The resolved presets directory path from configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public PresetManager(string presetsDirectory, ILogger<PresetManager> logger)
    {
        _presetsDirectory = presetsDirectory ?? throw new ArgumentNullException(nameof(presetsDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<SessionPreset>> LoadAllPresetsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading all presets from {Directory}", _presetsDirectory);
        Directory.CreateDirectory(_presetsDirectory);

        var presets = new List<SessionPreset>();
        var presetFiles = Directory.EnumerateFiles(_presetsDirectory, "*.json");

        foreach (var filePath in presetFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Preset loading was cancelled");
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
                        _logger.LogInformation(
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
                _logger.LogError(ex, "Failed to load or deserialize preset from {FilePath}", filePath);
            }
        }

        _logger.LogInformation("Successfully loaded {Count} presets", presets.Count);
        return presets;
    }

    public async Task<SessionPreset?> GetPresetByIdAsync(Guid presetId, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_presetsDirectory, $"{presetId}.json");

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Preset file not found: {FilePath}", filePath);
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var preset = await JsonSerializer.DeserializeAsync<SessionPreset>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (preset is not { Version: < CurrentPresetVersion }) return preset;

            _logger.LogInformation("Migrating preset '{PresetName}' from version {OldVersion} to {NewVersion}",
                preset.Name, preset.Version, CurrentPresetVersion);
            MigratePreset(preset);
            preset.Version = CurrentPresetVersion;
            await SavePresetAsync(preset, cancellationToken).ConfigureAwait(false);

            return preset;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load preset {PresetId} from {FilePath}", presetId, filePath);
            return null;
        }
    }

    public async Task SavePresetAsync(SessionPreset preset, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_presetsDirectory, $"{preset.Id}.json");
        var tempFilePath = Path.Combine(_presetsDirectory, $"{preset.Id}.json.tmp");
        _logger.LogInformation("Saving preset '{PresetName}' to {FilePath}", preset.Name, filePath);

        try
        {
            Directory.CreateDirectory(_presetsDirectory);

            // Write to temporary file first to ensure atomic operation
            await using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, preset, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Atomic rename: if this succeeds, the file is guaranteed to be complete
            File.Move(tempFilePath, filePath, overwrite: true);
            _logger.LogDebug("Preset '{PresetName}' saved successfully", preset.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save preset '{PresetName}'", preset.Name);

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
        var filePath = Path.Combine(_presetsDirectory, $"{presetId}.json");
        _logger.LogInformation("Deleting preset with ID {PresetId} from {FilePath}", presetId, filePath);

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Preset file deleted successfully");
            }
            else
            {
                _logger.LogWarning("Attempted to delete a preset that does not exist on disk: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete preset file {FilePath}", filePath);
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

        _logger.LogDebug("Preset migration completed for '{PresetName}'", preset.Name);
    }
}