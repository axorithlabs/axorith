using System.Text.Json;
using Axorith.Client.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

public sealed class UiSettingsStore(ILogger<UiSettingsStore> logger) : IClientUiSettingsStore
{
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "clientsettings.json");

    public ClientUiConfiguration LoadOrDefault()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new ClientUiConfiguration();
            }

            var json = File.ReadAllText(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ClientUiConfiguration();
            }

            var config = JsonSerializer.Deserialize<ClientUiConfiguration>(json);
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