using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Shared.ApplicationLauncher;
using Axorith.Shared.Platform;
using ObsSettings = Axorith.Module.ApplicationLauncher.Apps.OBS.Settings;

namespace Axorith.Module.ApplicationLauncher.Apps.OBS;

internal sealed class OBSApp : LauncherAppBase
{
    private const int MaxWebSocketConnectAttempts = 10;
    private const int InitialConnectDelayMs = 500;
    private const int MaxConnectDelayMs = 5_000;
    private readonly INotifier _notifier;
    private readonly Settings _settings;
    private readonly ObsWebSocketService _webSocketService;

    public OBSApp(
        IModuleLogger logger,
        IAppDiscoveryService appDiscovery,
        INotifier notifier,
        IPlatformProcessService processService,
        IPlatformWindowService windowService)
        : base(logger, processService, windowService)
    {
        _notifier = notifier;
        _settings = new Settings(appDiscovery);
        _webSocketService = new ObsWebSocketService(logger, _settings);
    }

    protected override LauncherSettingsBase Settings => _settings;
    protected override bool ReattachOnWindowTimeout => false;
    protected override string GetLaunchArguments() => "--disable-shutdown-check";
    protected override WindowConfigTimings GetWindowConfigTimings() => new(15_000, 500, 500, 500);

    public override async Task OnSessionStartAsync(CancellationToken cancellationToken)
    {
        await base.OnSessionStartAsync(cancellationToken).ConfigureAwait(false);
        if (!_settings.EnableWebSocket.GetCurrentValue())
        {
            return;
        }

        if (await ConnectWithRetryAsync(cancellationToken).ConfigureAwait(false))
        {
            await ExecuteActionAsync(_settings.SessionStartAction.GetCurrentValue(), cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _notifier.ShowToast(
                "OBS: WebSocket connection failed. Enable WebSocket in OBS: Tools → WebSocket Server Settings.",
                NotificationType.Error, "OBS");
        }
    }

    public override async Task OnSessionEndAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.EnableWebSocket.GetCurrentValue())
        {
            if (await _webSocketService.ConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                var action = _settings.SessionEndAction.GetCurrentValue();
                await ExecuteActionAsync(action, cancellationToken).ConfigureAwait(false);
                if (action != ObsSettings.ActionNone)
                {
                    await Task.Delay(1_500, cancellationToken).ConfigureAwait(false);
                }
            }

            await _webSocketService.DisconnectAsync().ConfigureAwait(false);
        }

        await base.OnSessionEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        try
        {
            _webSocketService.DisconnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _webSocketService.Dispose();
        base.Dispose();
    }

    private async Task<bool> ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var delay = InitialConnectDelayMs;
        for (var attempt = 1; attempt <= MaxWebSocketConnectAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.LogInfo("Attempting to connect to OBS WebSocket (attempt {Attempt}/{MaxAttempts})...",
                attempt, MaxWebSocketConnectAttempts);

            if (await _webSocketService.ConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (attempt < MaxWebSocketConnectAttempts)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = Math.Min(delay * 2, MaxConnectDelayMs);
            }
        }

        return false;
    }

    private async Task ExecuteActionAsync(string action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case ObsSettings.ActionStartStreaming:
                await _webSocketService.StartStreamingAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ObsSettings.ActionStartRecording:
                await _webSocketService.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ObsSettings.ActionStartBoth:
                await _webSocketService.StartStreamingAsync(cancellationToken).ConfigureAwait(false);
                await _webSocketService.StartRecordingAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ObsSettings.ActionStartVirtualCam:
                await _webSocketService.StartVirtualCameraAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ObsSettings.ActionStopStreaming:
                await _webSocketService.StopStreamingAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ObsSettings.ActionStopRecording:
                await _webSocketService.StopRecordingAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ObsSettings.ActionStopBoth:
                await _webSocketService.StopStreamingAsync(cancellationToken).ConfigureAwait(false);
                await _webSocketService.StopRecordingAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ObsSettings.ActionStopVirtualCam:
                await _webSocketService.StopVirtualCameraAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ObsSettings.ActionStopAll:
                await _webSocketService.StopStreamingAsync(cancellationToken).ConfigureAwait(false);
                await _webSocketService.StopRecordingAsync(cancellationToken).ConfigureAwait(false);
                await _webSocketService.StopVirtualCameraAsync(cancellationToken).ConfigureAwait(false);
                break;
        }
    }

}
