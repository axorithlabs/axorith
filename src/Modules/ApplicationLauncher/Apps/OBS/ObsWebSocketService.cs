using Axorith.Sdk.Logging;
using OBSWebsocketDotNet;
using System.Diagnostics;

namespace Axorith.Module.ApplicationLauncher.Apps.OBS;

internal sealed class ObsWebSocketService : IDisposable
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);
    private readonly IModuleLogger _logger;
    private readonly Settings _settings;
    private readonly OBSWebsocket _client = new() { WSTimeout = TimeSpan.FromSeconds(10) };

    public ObsWebSocketService(IModuleLogger logger, Settings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsIdentified)
        {
            return true;
        }

        var uri = $"ws://127.0.0.1:{_settings.GetPort()}";
        try
        {
            _logger.LogInfo("Connecting to OBS WebSocket at {Uri}...", uri);
            _client.ConnectAsync(uri, _settings.GetPassword() ?? string.Empty);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < ConnectionTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_client.IsIdentified)
                {
                    _logger.LogInfo("Connected to OBS WebSocket");
                    return true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }

            DisconnectClient();
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DisconnectClient();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to OBS WebSocket");
            DisconnectClient();
            return false;
        }
    }

    public Task DisconnectAsync()
    {
        DisconnectClient();
        return Task.CompletedTask;
    }

    public Task<bool> StartStreamingAsync(CancellationToken ct = default) =>
        SendRequestAsync("StartStream", _client.StartStream, ct);

    public Task<bool> StopStreamingAsync(CancellationToken ct = default) =>
        SendRequestAsync("StopStream", _client.StopStream, ct);

    public Task<bool> StartRecordingAsync(CancellationToken ct = default) =>
        SendRequestAsync("StartRecord", _client.StartRecord, ct);

    public Task<bool> StopRecordingAsync(CancellationToken ct = default) =>
        SendRequestAsync("StopRecord", () => _client.StopRecord(), ct);

    public Task<bool> StartVirtualCameraAsync(CancellationToken ct = default) =>
        SendRequestAsync("StartVirtualCam", _client.StartVirtualCam, ct);

    public Task<bool> StopVirtualCameraAsync(CancellationToken ct = default) =>
        SendRequestAsync("StopVirtualCam", _client.StopVirtualCam, ct);

    private async Task<bool> SendRequestAsync(string requestType, Action sendRequest, CancellationToken ct)
    {
        if (!_client.IsIdentified)
        {
            return false;
        }

        try
        {
            await Task.Run(sendRequest, ct).ConfigureAwait(false);
            _logger.LogInfo("Sent OBS request: {RequestType}", requestType);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OBS request: {RequestType}", requestType);
            return false;
        }
    }

    public void Dispose() => DisconnectClient();

    private void DisconnectClient()
    {
        try
        {
            _client.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to disconnect from OBS WebSocket: {Error}", ex.Message);
        }
    }
}
