using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axorith.Module.ApplicationLauncher.Apps.OBS;
using Axorith.Shared.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class ObsProtocolExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWebSocketClientAuthenticatesAndReportsTheResultOfEveryObsAction(bool rejectRequests)
    {
        var actual = await WithObsServerAsync(rejectRequests, async (port, ct) =>
        {
            using var settings = new Settings(Mock.Of<IAppDiscoveryService>(), new EmptySecureStorage());
            settings.WebSocketPort.SetValue(port);
            settings.WebSocketPassword.SetValue("fixture-password");
            using var service = new ObsWebSocketService(new TestModuleLogger(), settings);

            Assert.True(await service.ConnectAsync(ct));
            Assert.Equal(!rejectRequests, await service.StartStreamingAsync(ct));
            Assert.Equal(!rejectRequests, await service.StartRecordingAsync(ct));
            Assert.Equal(!rejectRequests, await service.StartVirtualCameraAsync(ct));
            Assert.Equal(!rejectRequests, await service.StopStreamingAsync(ct));
            Assert.Equal(!rejectRequests, await service.StopRecordingAsync(ct));
            Assert.Equal(!rejectRequests, await service.StopVirtualCameraAsync(ct));
            await service.DisconnectAsync();
        });
        Assert.Equal(new[] { "StartStream", "StartRecord", "StartVirtualCam", "StopStream", "StopRecord", "StopVirtualCam" }, actual);
    }

    [Theory]
    [InlineData("none", "")]
    [InlineData("unknown", "")]
    [InlineData("start_streaming", "StartStream")]
    [InlineData("start_recording", "StartRecord")]
    [InlineData("start_both", "StartStream|StartRecord")]
    [InlineData("start_virtualcam", "StartVirtualCam")]
    [InlineData("stop_streaming", "StopStream")]
    [InlineData("stop_recording", "StopRecord")]
    [InlineData("stop_both", "StopStream|StopRecord")]
    [InlineData("stop_virtualcam", "StopVirtualCam")]
    [InlineData("stop_all", "StopStream|StopRecord|StopVirtualCam")]
    public async Task AppLifecycleSendsExactlyTheSelectedActionsInOrder(string action, string expected)
    {
        foreach (var rejectRequests in new[] { false, true })
        {
            var actual = await WithObsServerAsync(rejectRequests, async (port, ct) =>
            {
                using var app = new OBSApp(new TestModuleLogger(), Mock.Of<IAppDiscoveryService>(),
                    Mock.Of<Axorith.Sdk.Services.INotifier>(), new EmptySecureStorage(),
                    Mock.Of<IPlatformProcessService>(), Mock.Of<IPlatformWindowService>());
                var settings = app.GetSettings().ToDictionary(setting => setting.Key);
                settings["EnableWebSocket"].SetValueFromObject(true);
                settings["WebSocketPort"].SetValueFromObject(port);
                settings["WebSocketPassword"].SetValueFromObject("fixture-password");
                settings["SessionEndAction"].SetValueFromObject(action);
                await app.OnSessionEndAsync(ct);
            });
            Assert.Equal(expected.Split('|', StringSplitOptions.RemoveEmptyEntries), actual);
        }
    }

    private static async Task<string[]> WithObsServerAsync(bool rejectRequests,
        Func<int, CancellationToken, Task> useClient)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var requests = new ConcurrentQueue<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string password = "fixture-password", salt = "fixture-salt", challenge = "fixture-challenge";
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        server.UseWebSockets();
        server.Map("/", async context =>
        {
            try
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                await Send(socket, new { op = 0, d = new { obsWebSocketVersion = "5.0.0", rpcVersion = 1,
                    authentication = new { salt, challenge } } });
                using var identify = await Receive(socket);
                Assert.NotNull(identify);
                Assert.Equal(1, identify.RootElement.GetProperty("op").GetInt32());
                var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
                Assert.Equal(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge))),
                    identify.RootElement.GetProperty("d").GetProperty("authentication").GetString());
                await Send(socket, new { op = 2, d = new { negotiatedRpcVersion = 1 } });
                while (true)
                {
                    using var message = await Receive(socket);
                    if (message is null) break;
                    Assert.Equal(6, message.RootElement.GetProperty("op").GetInt32());
                    var data = message.RootElement.GetProperty("d");
                    var requestType = data.GetProperty("requestType").GetString()!;
                    requests.Enqueue(requestType);
                    await Send(socket, new { op = 7, d = new { requestType,
                        requestId = data.GetProperty("requestId").GetString(),
                        requestStatus = new { result = !rejectRequests, code = rejectRequests ? 500 : 100 },
                        responseData = new { outputPath = "fixture-recording.mkv" } } });
                }
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", deadline.Token);
                completed.TrySetResult();
            }
            catch (Exception error) { completed.TrySetException(error); }
        });
        await server.StartAsync(deadline.Token);
        await useClient(new Uri(Assert.Single(server.Urls)).Port, deadline.Token);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        return [.. requests];

        Task Send(WebSocket socket, object value) => socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(value)),
            WebSocketMessageType.Text, true, deadline.Token);
        async Task<JsonDocument?> Receive(WebSocket socket)
        {
            var buffer = new byte[4096];
            using var body = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
                if (received.MessageType == WebSocketMessageType.Close) return null;
                body.Write(buffer, 0, received.Count);
                Assert.True(body.Length <= 16 * 1024);
            } while (!received.EndOfMessage);
            return JsonDocument.Parse(body.ToArray());
        }
    }
}
