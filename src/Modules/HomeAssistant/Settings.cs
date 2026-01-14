using System.Reactive.Disposables;
using System.Reactive.Linq;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.HomeAssistant;

internal sealed class Settings : IDisposable
{
    private readonly ISecureStorageService _secureStorage;
    private readonly CompositeDisposable _disposables = new();
    private const string TokenStorageKey = "HaAccessToken";

    private const string SetupInstructions =
        "HOW TO SETUP:\n" +
        "1. Go to HA Profile -> Long-Lived Access Tokens -> Create Token.\n" +
        "2. Paste the token below (it will be saved securely for all presets).\n" +
        "3. Enter Entity IDs for Start/End actions.\n" +
        "   - For Scripts: use 'script.your_script_name' (e.g. 'script.focus_mode').\n" +
        "   - For Scenes: use 'scene.your_scene_name' (e.g. 'scene.relax').\n" +
        "   - For Lights/Switches: use 'light.name' or 'switch.name'.\n" +
        "   - Scripts/Scenes/Automations will be TURNED ON.\n" +
        "   - Lights/Switches will be TURNED ON at Start and TURNED OFF at End (default behavior).";

    public Setting<string> Instructions { get; }

    public Setting<string> BaseUrl { get; }
    public Setting<string> AccessToken { get; }

    public Setting<string> StartEntityId { get; }
    public Setting<string> EndEntityId { get; }

    public Action TestConnectionAction { get; }

    private readonly IReadOnlyList<ISetting> _allSettings;
    private readonly IReadOnlyList<IAction> _allActions;

    public Settings(ISecureStorageService secureStorage)
    {
        _secureStorage = secureStorage;

        Instructions = Setting.AsTextArea(
            key: "Instructions",
            label: "Setup Guide",
            defaultValue: SetupInstructions,
            description: "Follow these steps to connect Axorith to Home Assistant.",
            isReadOnly: true
        );

        BaseUrl = Setting.AsText(
            key: "BaseUrl",
            label: "HA URL",
            defaultValue: "http://homeassistant.local:8123",
            description: "The address of your Home Assistant instance."
        );

        AccessToken = Setting.AsSecret(
            key: "AccessToken",
            label: "Access Token",
            description: "Long-Lived Access Token."
        );

        StartEntityId = Setting.AsText(
            key: "StartEntityId",
            label: "On Session Start (Entity ID)",
            defaultValue: "",
            description:
            "Entity to activate when session starts (e.g. 'script.focus', 'scene.work', 'light.desk'). Leave empty to do nothing."
        );

        EndEntityId = Setting.AsText(
            key: "EndEntityId",
            label: "On Session End (Entity ID)",
            defaultValue: "",
            description:
            "Entity to activate/deactivate when session ends. Scripts/Scenes are turned ON. Lights/Switches are turned OFF."
        );

        TestConnectionAction = Action.Create("TestConnection", "Test Connection");

        _allSettings =
        [
            Instructions,
            BaseUrl,
            AccessToken,
            StartEntityId,
            EndEntityId
        ];

        _allActions = [TestConnectionAction];

        var tokenSubscription = AccessToken.Value
            .Skip(1)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Subscribe(token => _secureStorage.StoreSecret(TokenStorageKey, token));

        _disposables.Add(tokenSubscription);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        Instructions.Dispose();
        BaseUrl.Dispose();
        AccessToken.Dispose();
        StartEntityId.Dispose();
        EndEntityId.Dispose();
        TestConnectionAction.Dispose();
    }

    public void LoadToken()
    {
        var token = _secureStorage.RetrieveSecret(TokenStorageKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            AccessToken.SetValue(token);
        }
    }

    public IReadOnlyList<ISetting> GetSettings()
    {
        return _allSettings;
    }

    public IReadOnlyList<IAction> GetActions()
    {
        return _allActions;
    }

    public Task<ValidationResult> ValidateAsync()
    {
        var errors = new Dictionary<string, string>();

        var baseUrl = BaseUrl.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            errors[BaseUrl.Key] = "Home Assistant URL is required.";
        }
        else if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            errors[BaseUrl.Key] = "Invalid URL format. Use http:// or https://";
        }

        var token = AccessToken.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = _secureStorage.RetrieveSecret(TokenStorageKey);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            errors[AccessToken.Key] = "Access Token is required.";
        }

        var startEntity = StartEntityId.GetCurrentValue();
        var endEntity = EndEntityId.GetCurrentValue();

        if (string.IsNullOrWhiteSpace(startEntity) && string.IsNullOrWhiteSpace(endEntity))
        {
            errors[StartEntityId.Key] = "At least one entity ID is required.";
        }

        return Task.FromResult(errors.Count > 0
            ? ValidationResult.Fail(errors, "Configuration contains errors.")
            : ValidationResult.Success);
    }
}