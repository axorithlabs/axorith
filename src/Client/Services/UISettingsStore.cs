using System.Text.Json;
using Axorith.Client.Services.Abstractions;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

public sealed class UiSettingsStore(ILogger<UiSettingsStore> logger) : IClientUiSettingsStore
{
    private readonly string _settingsPath = Path.Combine(ApplicationPaths.Config, "clientsettings.json");
    private readonly string _legacySettingsPath = Path.Combine(AppContext.BaseDirectory, "clientsettings.json");
    private const long MaxSettingsFileSizeBytes = 1 * 1024 * 1024; // 1 MB max

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        MaxDepth = 32 // Prevent stack overflow from deeply nested JSON
    };

    public ClientUiConfiguration LoadOrDefault()
    {
        var settingsPath = _settingsPath;
        try
        {
            if (!File.Exists(settingsPath))
            {
                settingsPath = _legacySettingsPath;
            }

            if (!File.Exists(settingsPath))
            {
                return LoadDefaults();
            }

            var fileInfo = new FileInfo(settingsPath);
            if (fileInfo.Length > MaxSettingsFileSizeBytes)
            {
                logger.LogWarning("Settings file {Path} exceeds maximum size limit ({Size} bytes)",
                    TelemetryGuard.SafePath(settingsPath),
                    fileInfo.Length);
                return new ClientUiConfiguration();
            }

            var json = File.ReadAllText(settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ClientUiConfiguration();
            }

            // V5611: System.Text.Json is safe - no polymorphic deserialization or type name handling
            // File size and MaxDepth are validated to prevent DoS attacks
            var config = JsonSerializer.Deserialize<ClientUiConfiguration>(json, DeserializeOptions); //-V5611
            config ??= new ClientUiConfiguration();
            if (TelemetryPreference.TryRead(out var telemetryEnabled))
            {
                config.TelemetryEnabled = telemetryEnabled;
            }
            return config;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load client UI settings from {Path}",
                TelemetryGuard.SafePath(settingsPath));
            return LoadDefaults();
        }
    }

    private static ClientUiConfiguration LoadDefaults()
    {
        var configuration = new ClientUiConfiguration();
        if (TelemetryPreference.TryRead(out var telemetryEnabled))
        {
            configuration.TelemetryEnabled = telemetryEnabled;
        }

        return configuration;
    }

    public bool Save(ClientUiConfiguration configuration)
    {
        try
        {
            var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, json);
            return TelemetryPreference.Save(configuration.TelemetryEnabled);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save client UI settings to {Path}",
                TelemetryGuard.SafePath(_settingsPath));
            return false;
        }
    }
}
