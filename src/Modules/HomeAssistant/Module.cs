using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Http;
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

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _settings.GetSettings();
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _settings.GetActions();
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        _settings.LoadToken();
        return Task.CompletedTask;
    }

    public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
    {
        return _settings.ValidateAsync();
    }

    public async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        _settings.LoadToken();

        var entityId = _settings.StartEntityId.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        await CallServiceAsync("turn_on", entityId, cancellationToken);
    }

    public async Task OnSessionEndAsync(CancellationToken cancellationToken)
    {
        _settings.LoadToken();

        var entityId = _settings.EndEntityId.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        var domain = entityId.Split('.')[0].ToLowerInvariant();
        var service = "turn_off";

        if (domain is "script" or "scene" or "automation" or "input_button")
        {
            service = "turn_on";
        }

        await CallServiceAsync(service, entityId, cancellationToken);
    }

    private async Task CallServiceAsync(string service, string entityId, CancellationToken ct)
    {
        var url = _settings.BaseUrl.GetCurrentValue();
        var token = _settings.AccessToken.GetCurrentValue();

        await _client.CallServiceAsync(url, token, "homeassistant", service, entityId, ct);
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
            await Task.Delay(3000);
            _settings.TestConnectionAction.SetLabel("Test Connection");
            _settings.TestConnectionAction.SetEnabled(true);
        }
    }

    public void Dispose()
    {
        _settings.Dispose();
        GC.SuppressFinalize(this);
    }
}
