using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;

namespace Axorith.Module.HomeAssistant;

public class Module : IModule
{
    private readonly IModuleLogger _logger;
    private readonly Settings _settings;
    private readonly HaClient _client;

    public Module(IModuleLogger logger, IHttpClientFactory httpClientFactory, ISecureStorageService secureStorage)
    {
        _logger = logger;
        _settings = new Settings(secureStorage);
        _client = new HaClient(httpClientFactory, logger);

        _settings.TestConnectionAction.OnInvokeAsync(TestConnectionAsync);
    }

    public IReadOnlyList<ISetting> GetSettings() => _settings.GetSettings();

    public IReadOnlyList<IAction> GetActions() => _settings.GetActions();

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        _settings.LoadToken();
        return Task.CompletedTask;
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken) => _settings.ValidateAsync();

    public Task OnSessionStartAsync(CancellationToken cancellationToken) =>
        CallServiceAsync(_settings.StartEntityId, ending: false, cancellationToken);

    public Task OnSessionEndAsync(CancellationToken cancellationToken) =>
        CallServiceAsync(_settings.EndEntityId, ending: true, cancellationToken);

    private async Task CallServiceAsync(Setting<string> setting, bool ending, CancellationToken ct)
    {
        _settings.LoadToken();
        var entityId = setting.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(entityId)) return;
        var turnOn = !ending || entityId.Split('.')[0].ToLowerInvariant() is
            "script" or "scene" or "automation" or "input_button";
        await _client.CallServiceAsync(_settings.BaseUrl.GetCurrentValue(), _settings.AccessToken.GetCurrentValue(),
            "homeassistant", turnOn ? "turn_on" : "turn_off", entityId, ct);
    }

    private async Task TestConnectionAsync()
    {
        _settings.TestConnectionAction.SetLabel("Testing...");
        _settings.TestConnectionAction.SetEnabled(false);

        try
        {
            var url = _settings.BaseUrl.GetCurrentValue();
            var token = _settings.AccessToken.GetCurrentValue();

            if (string.IsNullOrWhiteSpace(url))
            {
                _settings.TestConnectionAction.SetLabel("Error: URL required");
                return;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                _settings.TestConnectionAction.SetLabel("Error: Token required");
                return;
            }

            var success = await _client.CheckConnectionAsync(url, token, CancellationToken.None);

            _settings.TestConnectionAction.SetLabel(success ? "Connected OK" : "Connection Failed");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Test connection failed - network error");
            _settings.TestConnectionAction.SetLabel("Network Error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test connection failed");
            _settings.TestConnectionAction.SetLabel("Error");
        }
        finally
        {
            _settings.TestConnectionAction.SetEnabled(true);
        }
    }

    public void Dispose()
    {
        _settings.Dispose();
        GC.SuppressFinalize(this);
    }
}
