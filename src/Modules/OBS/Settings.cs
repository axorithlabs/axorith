using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.OBS;

internal sealed class Settings : LauncherSettingsBase
{
    private const int MinPort = 1;
    private const int MaxPort = 65535;
    private const int DefaultObsWebSocketPort = 4455;

    internal const string ActionNone = "none";
    internal const string ActionStartStreaming = "start_streaming";
    internal const string ActionStartRecording = "start_recording";
    internal const string ActionStartBoth = "start_both";
    internal const string ActionStartVirtualCam = "start_virtualcam";
    internal const string ActionStopStreaming = "stop_streaming";
    internal const string ActionStopRecording = "stop_recording";
    internal const string ActionStopBoth = "stop_both";
    internal const string ActionStopVirtualCam = "stop_virtualcam";
    internal const string ActionStopAll = "stop_all";

    public override Setting<string> ApplicationPath => ObsPath;

    public Setting<string> ObsPath { get; }
    public Action RefreshPathAction { get; }

    public Setting<bool> EnableWebSocket { get; }
    public Setting<int> WebSocketPort { get; }
    public Setting<string> WebSocketPassword { get; }

    public Setting<string> SessionStartAction { get; }
    public Setting<string> SessionEndAction { get; }

    private readonly IAppDiscoveryService _appDiscovery;

    public Settings(IAppDiscoveryService appDiscovery)
    {
        _appDiscovery = appDiscovery;

        ObsPath = Setting.AsChoice(
            key: "ObsPath",
            label: "OBS Studio Executable",
            defaultValue: string.Empty,
            initialChoices: [new KeyValuePair<string, string>("", "Scanning for OBS...")],
            description: "Path to OBS Studio executable."
        );

        RefreshPathAction = Action.Create("RefreshPath", "Refresh Path");
        RefreshPathAction.OnInvokeAsync(RefreshPathAsync);

        EnableWebSocket = Setting.AsCheckbox(
            key: "EnableWebSocket",
            label: "Enable WebSocket Control",
            defaultValue: false,
            description:
            "Control OBS via WebSocket. To enable: Open OBS → Tools → WebSocket Server Settings → Enable WebSocket server → Copy port and password."
        );

        WebSocketPort = Setting.AsInt(
            key: "WebSocketPort",
            label: "WebSocket Port",
            defaultValue: DefaultObsWebSocketPort,
            description: "OBS WebSocket server port. Find it in OBS: Tools → WebSocket Server Settings.",
            isVisible: false
        );

        WebSocketPassword = Setting.AsText(
            key: "WebSocketPassword",
            label: "WebSocket Password",
            defaultValue: string.Empty,
            description:
            "OBS WebSocket password. Find it in OBS: Tools → WebSocket Server Settings → Show Connect Info.",
            isVisible: false
        );

        SessionStartAction = Setting.AsChoice(
            key: "SessionStartAction",
            label: "Action on Session Start",
            defaultValue: ActionNone,
            initialChoices:
            [
                new KeyValuePair<string, string>(ActionNone, "None"),
                new KeyValuePair<string, string>(ActionStartStreaming, "Start Streaming"),
                new KeyValuePair<string, string>(ActionStartRecording, "Start Recording"),
                new KeyValuePair<string, string>(ActionStartBoth, "Start Streaming + Recording"),
                new KeyValuePair<string, string>(ActionStartVirtualCam, "Start Virtual Camera")
            ],
            description: "Action to perform when session starts.",
            isVisible: false
        );

        SessionEndAction = Setting.AsChoice(
            key: "SessionEndAction",
            label: "Action on Session End",
            defaultValue: ActionNone,
            initialChoices:
            [
                new KeyValuePair<string, string>(ActionNone, "None (Keep Running)"),
                new KeyValuePair<string, string>(ActionStopStreaming, "Stop Streaming"),
                new KeyValuePair<string, string>(ActionStopRecording, "Stop Recording"),
                new KeyValuePair<string, string>(ActionStopBoth, "Stop Streaming + Recording"),
                new KeyValuePair<string, string>(ActionStopVirtualCam, "Stop Virtual Camera"),
                new KeyValuePair<string, string>(ActionStopAll, "Stop Everything")
            ],
            description: "Action to perform when session ends.",
            isVisible: false
        );

        EnableWebSocket.Value.Subscribe(enabled =>
        {
            WebSocketPort.SetVisibility(enabled);
            WebSocketPassword.SetVisibility(enabled);
            SessionStartAction.SetVisibility(enabled);
            SessionEndAction.SetVisibility(enabled);
        });

        SetupBaseReactiveVisibility();
    }

    protected override IEnumerable<ISetting> GetAdditionalSettings()
    {
        yield return EnableWebSocket;
        yield return WebSocketPort;
        yield return WebSocketPassword;
        yield return SessionStartAction;
        yield return SessionEndAction;
    }

    protected override IEnumerable<IAction> GetAdditionalActions()
    {
        yield return RefreshPathAction;
    }

    protected override async Task InitializeAdditionalAsync()
    {
        await RefreshPathAsync();
    }

    public int GetPort()
    {
        return WebSocketPort.GetCurrentValue();
    }

    public string? GetPassword()
    {
        var pwd = WebSocketPassword.GetCurrentValue();
        return string.IsNullOrWhiteSpace(pwd) ? null : pwd;
    }

    protected override Task<ValidationResult> ValidateAdditionalAsync()
    {
        if (!EnableWebSocket.GetCurrentValue())
        {
            return Task.FromResult(ValidationResult.Success);
        }

        var port = WebSocketPort.GetCurrentValue();
        if (port < MinPort || port > MaxPort)
        {
            return Task.FromResult(ValidationResult.Fail(
                new Dictionary<string, string>
                    { [WebSocketPort.Key] = $"Port must be between {MinPort} and {MaxPort}." },
                "Invalid WebSocket port."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    private async Task RefreshPathAsync()
    {
        var possiblePaths = new[]
        {
            Path.Combine(ApplicationPaths.ProgramFiles, "obs-studio", "bin", "64bit", "obs64.exe"),
            Path.Combine(ApplicationPaths.ProgramFilesX86, "obs-studio", "bin", "64bit", "obs64.exe"),
            Path.Combine(ApplicationPaths.ProgramFiles, "obs-studio", "bin", "32bit", "obs32.exe"),
            @"C:\Program Files (x86)\Steam\steamapps\common\OBS Studio\bin\64bit\obs64.exe"
        };

        var path = possiblePaths.FirstOrDefault(File.Exists);

        if (string.IsNullOrEmpty(path))
        {
            path = await Task.Run(() => _appDiscovery.FindKnownApp("obs64.exe", "OBS Studio")).ConfigureAwait(false);
        }

        var choices = new List<KeyValuePair<string, string>>
        {
            !string.IsNullOrEmpty(path)
                ? new KeyValuePair<string, string>(path, "OBS Studio (Auto-Detected)")
                : new KeyValuePair<string, string>("", "OBS Studio not found")
        };

        var current = ObsPath.GetCurrentValue();
        if (!string.IsNullOrEmpty(current) && choices.All(c => c.Key != current))
        {
            choices.Insert(0, new KeyValuePair<string, string>(current, $"{current} (Custom)"));
        }

        ObsPath.SetChoices(choices);

        if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(path))
        {
            ObsPath.SetValue(path);
        }
    }

    public override void Dispose()
    {
        ObsPath.Dispose();
        RefreshPathAction.Dispose();
        EnableWebSocket.Dispose();
        WebSocketPort.Dispose();
        WebSocketPassword.Dispose();
        SessionStartAction.Dispose();
        SessionEndAction.Dispose();
        base.Dispose();
    }
}