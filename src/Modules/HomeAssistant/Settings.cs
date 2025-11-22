using System.Reactive.Linq;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Services;
using Axorith.Sdk.Settings;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.HomeAssistant;

internal sealed class Settings
{
    private readonly ISecureStorageService _secureStorage;
    private const string TokenStorageKey = "HaAccessToken";

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

        var helpText =
            "HOW TO SETUP:\n" +
            "1. Go to HA Profile -> Long-Lived Access Tokens -> Create Token.\n" +
            "2. Paste the token below (it will be saved securely for all presets).\n" +
            "3. Enter Entity IDs for Start/End actions.\n" +
            "   - For Scripts: use 'script.your_script_name' (e.g. 'script.focus_mode').\n" +
            "   - For Scenes: use 'scene.your_scene_name' (e.g. 'scene.relax').\n" +
            "   - For Lights/Switches: use 'light.name' or 'switch.name'.\n" +
            "   - Scripts/Scenes/Automations will be TURNED ON.\n" +
            "   - Lights/Switches will be TURNED ON at Start and TURNED OFF at End (default behavior).";

        Instructions = Setting.AsTextArea(
            key: "Instructions",
            label: "Setup Guide",
            defaultValue: helpText,
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
            description: "Long-Lived Access Token. Stored securely in Windows Credential Manager."
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

        AccessToken.Value
            .Skip(1)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Subscribe(token => _secureStorage.StoreSecret(TokenStorageKey, token));
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
        if (string.IsNullOrWhiteSpace(BaseUrl.GetCurrentValue()))
        {
            return Task.FromResult(ValidationResult.Fail("Home Assistant URL is required."));
        }

        var token = AccessToken.GetCurrentValue();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = _secureStorage.RetrieveSecret(TokenStorageKey);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(ValidationResult.Fail("Access Token is required."));
        }

        return Task.FromResult(ValidationResult.Success);
    }
}