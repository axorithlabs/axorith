using System.IO;
using System.Text.Json;
using Axorith.Client;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

public interface IClientUiSettingsStore
{
    ClientUiConfiguration LoadOrDefault();
    void Save(ClientUiConfiguration configuration);
}

public sealed class UISettingsStore : IClientUiSettingsStore
{
    private readonly ILogger<UISettingsStore> _logger;
    private readonly string _settingsPath;

    public UISettingsStore(ILogger<UISettingsStore> logger)
    {
        _logger = logger;
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "clientsettings.json");
    }

    public ClientUiConfiguration LoadOrDefault()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new ClientUiConfiguration();

            var json = File.ReadAllText(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
                return new ClientUiConfiguration();

            var config = JsonSerializer.Deserialize<ClientUiConfiguration>(json);
            return config ?? new ClientUiConfiguration();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load client UI settings from {Path}", _settingsPath);
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
            _logger.LogWarning(ex, "Failed to save client UI settings to {Path}", _settingsPath);
        }
    }
}
