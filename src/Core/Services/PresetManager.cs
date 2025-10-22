using System.Text.Json;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Axorith.Core.Services;

/// <summary>
///     The concrete implementation for managing session presets using JSON files on disk.
/// </summary>
public class PresetManager(ILogger<PresetManager> logger) : IPresetManager
{
    private readonly string _presetsDirectory = GetPresetsDirectoryPath();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public async Task<IReadOnlyList<SessionPreset>> LoadAllPresetsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading all presets from {Directory}", _presetsDirectory);
        Directory.CreateDirectory(_presetsDirectory);

        var presets = new List<SessionPreset>();
        var presetFiles = Directory.EnumerateFiles(_presetsDirectory, "*.json");

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
                if (preset != null) presets.Add(preset);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load or deserialize preset from {FilePath}", filePath);
            }
        }

        logger.LogInformation("Successfully loaded {Count} presets", presets.Count);
        return presets;
    }

    public async Task SavePresetAsync(SessionPreset preset, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_presetsDirectory, $"{preset.Id}.json");
        logger.LogInformation("Saving preset '{PresetName}' to {FilePath}", preset.Name, filePath);

        try
        {
            Directory.CreateDirectory(_presetsDirectory);
            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, preset, _jsonOptions, cancellationToken);
            logger.LogDebug("Preset '{PresetName}' saved successfully", preset.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save preset '{PresetName}'", preset.Name);
        }
    }

    public Task DeletePresetAsync(Guid presetId, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_presetsDirectory, $"{presetId}.json");
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

    private static string GetPresetsDirectoryPath()
    {
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataFolder, "Axorith", "presets");
    }
}