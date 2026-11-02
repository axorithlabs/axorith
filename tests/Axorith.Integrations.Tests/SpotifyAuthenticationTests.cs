using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Axorith.Module.ApplicationLauncher.Apps.Spotify;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Axorith.Shared.Platform;
using Moq;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class SpotifyAuthenticationTests
{
    [Fact]
    public async Task PlaybackSelectsTheConfiguredDeviceSendsItsOptionsAndPausesOnlyTheSessionItStarted()
    {
        var storage = new TokenStorage();
        var logger = new ErrorLogger();
        using var endpoint = new PlaybackEndpoint();
        using var http = new HttpClient(endpoint) { Timeout = TimeSpan.FromSeconds(5) };
        using var settings = new Settings(Mock.Of<IAppDiscoveryService>());
        settings.DeviceSelectionMode.SetValue(Settings.ModeSpecificName);
        settings.SpecificDeviceName.SetValue("desk speaker");
        settings.CustomUrl.SetValue("https://open.spotify.com/playlist/fixture-playlist?si=fixture");
        settings.Volume.SetValue(120);
        settings.Shuffle.SetValue("true");
        settings.RepeatMode.SetValue("track");
        var processes = new Mock<IPlatformProcessService>();
        processes.Setup(value => value.FindProcesses("Spotify")).Returns(() => [Process.GetCurrentProcess()]);
        var factory = new ClientFactory(http);
        using var auth = new AuthService(logger, factory, storage, settings, new NoopNotifier());
        var api = new SpotifyApiService(factory, auth, logger);
        using var playback = new PlaybackService(logger, settings, auth, api, processes.Object);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await playback.OnSessionStartAsync(deadline.Token).WaitAsync(TimeSpan.FromSeconds(10));
        await playback.OnSessionEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await playback.OnSessionEndAsync().WaitAsync(TimeSpan.FromSeconds(5));

        var calls = endpoint.Requests.ToArray();
        Assert.Single(calls, call => call.Uri.Host == "accounts.spotify.com");
        var play = Assert.Single(calls, call => call.Uri.AbsolutePath == "/v1/me/player/play");
        Assert.Equal("PUT", play.Method);
        Assert.Contains("device_id=target", play.Uri.Query);
        using var body = JsonDocument.Parse(play.Body);
        Assert.Equal("spotify:playlist:fixture-playlist", body.RootElement.GetProperty("context_uri").GetString());
        foreach (var (path, query) in new[] { ("volume", "volume_percent=100"), ("shuffle", "state=true"), ("repeat", "state=track") })
        {
            var request = Assert.Single(calls, call => call.Uri.AbsolutePath == "/v1/me/player/" + path);
            Assert.Equal("PUT", request.Method);
            Assert.Contains("device_id=target", request.Uri.Query);
            Assert.Contains(query, request.Uri.Query);
        }
        var pause = Assert.Single(calls, call => call.Uri.AbsolutePath == "/v1/me/player/pause");
        Assert.Contains("device_id=target", pause.Uri.Query);
        Assert.All(calls.Where(call => call.Uri.Host == "api.spotify.com"), call => Assert.Equal("Bearer access-token", call.Authorization));
        Assert.Equal("new-refresh", storage.RetrieveSecret("SpotifyRefreshToken"));
        Assert.Empty(logger.Errors);
        processes.Verify(value => value.FindProcesses("Spotify"), Times.Once);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOneRefreshAndRotateThePersistedToken()
    {
        var storage = new TokenStorage();
        var logger = new ErrorLogger();
        using var handler = new TokenEndpoint();
        using var client = new HttpClient(handler);
        using var settings = new Settings(Mock.Of<IAppDiscoveryService>());
        using var auth = new AuthService(logger, new ClientFactory(client), storage, settings, new NoopNotifier());
        var tokens = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => auth.GetValidAccessTokenAsync()));

        Assert.All(tokens, token => Assert.Equal("access-token", token));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://accounts.spotify.com/api/token", request.Url);
        Assert.Contains("grant_type=refresh_token", request.Body);
        Assert.Contains("refresh_token=old-refresh", request.Body);
        Assert.Equal("new-refresh", storage.RetrieveSecret("SpotifyRefreshToken"));
        Assert.Equal("Authenticated", settings.AuthStatus.GetCurrentValue());
        Assert.Empty(logger.Errors);

        await settings.LogoutAction.InvokeAsync();
        Assert.Null(storage.RetrieveSecret("SpotifyRefreshToken"));
        Assert.Null(await auth.GetValidAccessTokenAsync());
        Assert.Equal("Login required", settings.AuthStatus.GetCurrentValue());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DisposeDuringPendingRefreshDoesNotFaultTheRunningOrQueuedRequest()
    {
        var storage = new TokenStorage();
        var logger = new ErrorLogger();
        using var handler = new TokenEndpoint { WaitForRelease = true };
        using var client = new HttpClient(handler);
        using var settings = new Settings(Mock.Of<IAppDiscoveryService>());
        using var auth = new AuthService(logger, new ClientFactory(client), storage, settings, new NoopNotifier());
        var running = auth.GetValidAccessTokenAsync();
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = auth.GetValidAccessTokenAsync();
        auth.Dispose();
        handler.Release.TrySetResult();

        var tokens = await Task.WhenAll(running, queued).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(tokens, token => Assert.Equal("access-token", token));
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(logger.Errors, error => error is ObjectDisposedException);
    }

    [Fact]
    public async Task RejectedRefreshReturnsNoTokenAndPreservesTheStoredCredentialForRetry()
    {
        var storage = new TokenStorage();
        var logger = new ErrorLogger();
        using var handler = new TokenEndpoint { Status = HttpStatusCode.Unauthorized };
        using var client = new HttpClient(handler);
        using var settings = new Settings(Mock.Of<IAppDiscoveryService>());
        using var auth = new AuthService(logger, new ClientFactory(client), storage, settings, new NoopNotifier());

        Assert.Null(await auth.GetValidAccessTokenAsync());
        Assert.Equal("old-refresh", storage.RetrieveSecret("SpotifyRefreshToken"));
        Assert.Equal("Authenticated", settings.AuthStatus.GetCurrentValue());
        Assert.IsType<HttpRequestException>(Assert.Single(logger.Errors));
    }

    private sealed class TokenStorage : ISecureStorageService
    {
        private readonly ConcurrentDictionary<string, string> _values = new([KeyValuePair.Create("SpotifyRefreshToken", "old-refresh")]);
        public void StoreSecret(string key, string secret) => _values[key] = secret;
        public string? RetrieveSecret(string key) => _values.GetValueOrDefault(key);
        public void DeleteSecret(string key) => _values.TryRemove(key, out _);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TokenEndpoint : HttpMessageHandler
    {
        public bool WaitForRelease { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public ConcurrentQueue<(string Url, string Body)> Requests { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue((request.RequestUri!.AbsoluteUri, await request.Content!.ReadAsStringAsync(cancellationToken)));
            Entered.TrySetResult();
            if (WaitForRelease) await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent("{\"access_token\":\"access-token\",\"expires_in\":3600,\"refresh_token\":\"new-refresh\"}")
            };
        }
    }

    private sealed class ErrorLogger : IModuleLogger
    {
        public ConcurrentQueue<Exception> Errors { get; } = new();
        public void LogDebug(string messageTemplate, params object[] args) { }
        public void LogInfo(string messageTemplate, params object[] args) { }
        public void LogWarning(string messageTemplate, params object[] args) { }
        public void LogError(Exception? exception, string messageTemplate, params object[] args) { if (exception is not null) Errors.Enqueue(exception); }
        public void LogFatal(Exception? exception, string messageTemplate, params object[] args) { if (exception is not null) Errors.Enqueue(exception); }
    }

    private sealed class PlaybackEndpoint : HttpMessageHandler
    {
        public ConcurrentQueue<(string Method, Uri Uri, string? Authorization, string Body)> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Enqueue((request.Method.Method, uri, request.Headers.Authorization?.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            var json = uri.AbsolutePath switch
            {
                "/api/token" => "{\"access_token\":\"access-token\",\"expires_in\":3600,\"refresh_token\":\"new-refresh\"}",
                "/v1/me/player/devices" => "{\"devices\":[{\"id\":\"other\",\"name\":\"Other computer\",\"type\":\"Computer\",\"is_active\":false},{\"id\":\"target\",\"name\":\"Desk Speaker\",\"type\":\"Speaker\",\"is_active\":true}]}",
                "/v1/me/playlists" or "/v1/me/albums" or "/v1/me/tracks" => "{\"items\":[]}",
                "/v1/me/player/play" or "/v1/me/player/pause" or "/v1/me/player/volume" or "/v1/me/player/shuffle" or "/v1/me/player/repeat" => null,
                _ => throw new InvalidOperationException("Unexpected Spotify request: " + uri)
            };
            return json is null ? new HttpResponseMessage(HttpStatusCode.NoContent) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
