using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axorith.Sdk.Logging;

namespace Axorith.Module.OBS;

/// <summary>
/// Service for communicating with OBS via WebSocket (obs-websocket 5.x protocol).
/// </summary>
internal sealed class ObsWebSocketService(IModuleLogger logger, Settings settings) : IObsWebSocketService
{
    private ClientWebSocket? _webSocket;
    private int _messageId;
    private bool _isConnected;

    private const int ConnectionTimeoutMs = 5000;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected && _webSocket?.State == WebSocketState.Open)
        {
            return true;
        }

        // Reset state before reconnection attempt
        _isConnected = false;

        var port = settings.GetPort();
        var password = settings.GetPassword();
        var uri = new Uri($"ws://127.0.0.1:{port}");

        try
        {
            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();

            logger.LogInfo("Connecting to OBS WebSocket at {Uri}...", uri);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ConnectionTimeoutMs);

            await _webSocket.ConnectAsync(uri, cts.Token).ConfigureAwait(false);

            // Receive Hello
            var helloResponse = await ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
            if (helloResponse == null) return false;

            using var helloDoc = JsonDocument.Parse(helloResponse);
            if (helloDoc.RootElement.GetProperty("op").GetInt32() != 0) return false;

            var data = helloDoc.RootElement.GetProperty("d");
            string identifyJson;

            if (data.TryGetProperty("authentication", out var authElement))
            {
                var challenge = authElement.GetProperty("challenge").GetString()!;
                var salt = authElement.GetProperty("salt").GetString()!;
                var authString = GenerateAuthString(password ?? string.Empty, challenge, salt);

                identifyJson = JsonSerializer.Serialize(new { op = 1, d = new { rpcVersion = 1, authentication = authString } });
            }
            else
            {
                identifyJson = JsonSerializer.Serialize(new { op = 1, d = new { rpcVersion = 1 } });
            }

            await SendMessageAsync(identifyJson, cancellationToken).ConfigureAwait(false);

            // Receive Identified
            var identifiedResponse = await ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
            if (identifiedResponse == null) return false;

            using var identifiedDoc = JsonDocument.Parse(identifiedResponse);
            if (identifiedDoc.RootElement.GetProperty("op").GetInt32() != 2) return false;

            _isConnected = true;
            logger.LogInfo("Connected to OBS WebSocket");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to OBS WebSocket");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            try { await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None); }
            catch { }
        }
        _isConnected = false;
    }

    public Task<bool> StartStreamingAsync(CancellationToken ct = default) => SendRequestAsync("StartStream", null, ct);
    public Task<bool> StopStreamingAsync(CancellationToken ct = default) => SendRequestAsync("StopStream", null, ct);
    public Task<bool> StartRecordingAsync(CancellationToken ct = default) => SendRequestAsync("StartRecord", null, ct);
    public Task<bool> StopRecordingAsync(CancellationToken ct = default) => SendRequestAsync("StopRecord", null, ct);
    public Task<bool> StartVirtualCameraAsync(CancellationToken ct = default) => SendRequestAsync("StartVirtualCam", null, ct);
    public Task<bool> StopVirtualCameraAsync(CancellationToken ct = default) => SendRequestAsync("StopVirtualCam", null, ct);

    private async Task<bool> SendRequestAsync(string requestType, object? requestData, CancellationToken cancellationToken)
    {
        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
        {
            return false;
        }

        var requestId = Interlocked.Increment(ref _messageId).ToString();
        var message = new { op = 6, d = new { requestType, requestId, requestData } };

        try
        {
            await SendMessageAsync(JsonSerializer.Serialize(message), cancellationToken).ConfigureAwait(false);
            logger.LogInfo("Sent OBS request: {RequestType}", requestType);

            var response = await ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
            if (response != null)
            {
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.GetProperty("op").GetInt32() == 7)
                {
                    var result = doc.RootElement.GetProperty("d").GetProperty("requestStatus").GetProperty("result").GetBoolean();
                    return result;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send OBS request: {RequestType}", requestType);
            return false;
        }
    }

    private async Task SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        if (_webSocket == null) return;
        var bytes = Encoding.UTF8.GetBytes(message);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task<string?> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        if (_webSocket == null) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(10000);

        try
        {
            using var ms = new MemoryStream();
            var buffer = new byte[4096];
            WebSocketReceiveResult result;

            do
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _isConnected = false;
                    return null;
                }
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateAuthString(string password, string challenge, string salt)
    {
        using var sha256 = SHA256.Create();
        var passwordSaltHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + salt));
        var base64Secret = Convert.ToBase64String(passwordSaltHash);
        var authHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(base64Secret + challenge));
        return Convert.ToBase64String(authHash);
    }

    public void Dispose()
    {
        _webSocket?.Dispose();
    }
}
