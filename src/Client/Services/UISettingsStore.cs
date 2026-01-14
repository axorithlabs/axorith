using System.Text.Json;
using Axorith.Client.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

public sealed class UiSettingsStore(ILogger<UiSettingsStore> logger) : IClientUiSettingsStore
{
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "clientsettings.json");
    private const long MaxSettingsFileSizeBytes = 1 * 1024 * 1024; // 1 MB max

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        MaxDepth = 32 // Prevent stack overflow from deeply nested JSON
    };

    public ClientUiConfiguration LoadOrDefault()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new ClientUiConfiguration();
            }

            var fileInfo = new FileInfo(_settingsPath);
            if (fileInfo.Length > MaxSettingsFileSizeBytes)
            {
                logger.LogWarning("Settings file {Path} exceeds maximum size limit ({Size} bytes)", _settingsPath,
                    fileInfo.Length);
                return new ClientUiConfiguration();
            }

            var json = File.ReadAllText(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ClientUiConfiguration();
            }

            // V5611: System.Text.Json is safe - no polymorphic deserialization or type name handling
            // File size and MaxDepth are validated to prevent DoS attacks
            var config = JsonSerializer.Deserialize<ClientUiConfiguration>(json, DeserializeOptions); //-V5611
            return config ?? new ClientUiConfiguration();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load client UI settings from {Path}", _settingsPath);
            return new ClientUiConfiguration();
        }
    }

    public void Save(ClientUiConfiguration configuration)
    {
        try
        {
            var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save client UI settings to {Path}", _settingsPath);
        }
    }
}