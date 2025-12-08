using System.Diagnostics;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axorith.Sdk;
using Axorith.Sdk.Http;
using Axorith.Sdk.Logging;
using Axorith.Sdk.Services;
using Action = Axorith.Sdk.Actions.Action;

namespace Axorith.Module.SpotifyPlayer;

internal sealed class AuthService : IDisposable
{
    private readonly IModuleLogger _logger;
    private readonly IHttpClient _authClient;
    private readonly ISecureStorageService _secureStorage;
    private readonly Settings _settings;
    private readonly INotifier _notifier;
    private readonly CompositeDisposable _disposables = [];
    private readonly SemaphoreSlim _tokenRefreshSemaphore = new(1, 1);

    private string? _inMemoryAccessToken;
    private DateTimeOffset? _accessTokenExpiresAtUtc;

    private const string RefreshTokenKey = "SpotifyRefreshToken";
    private const string SpotifyClientId = "b9335aa114364ba8b957b44d33bb735d";

    private static readonly int[] AllowedPorts = Enumerable.Range(8888, 8).ToArray(); // 8888 to 8895
    private const string RedirectPath = "/callback/";

    private readonly Action _loginAction;
    private readonly Action _logoutAction;

    public event Action<bool>? AuthenticationStateChanged;

    public AuthService(IModuleLogger logger, IHttpClientFactory httpClientFactory,
        ISecureStorageService secureStorage, ModuleDefinition definition, Settings settings, INotifier notifier)
    {
        _logger = logger;
        _secureStorage = secureStorage;
        _settings = settings;
        _notifier = notifier;
        _authClient = httpClientFactory.CreateClient($"{definition.Name}.Auth");

        _loginAction = _settings.LoginAction;
        _logoutAction = _settings.LogoutAction;

        RefreshUiState();

        _loginAction.Invoked
            .SelectMany(_ => PerformPkceLoginAsync())
            .Subscribe(UpdateUiForAuthenticationState)
            .DisposeWith(_disposables);

        _logoutAction.Invoked
            .Subscribe(_ =>
            {
                Logout();
                UpdateUiForAuthenticationState(false);
            })
            .DisposeWith(_disposables);
    }

    public bool HasRefreshToken()
    {
        return !string.IsNullOrWhiteSpace(_secureStorage.RetrieveSecret(RefreshTokenKey));
    }

    public void RefreshUiState()
    {
        UpdateUiForAuthenticationState(HasRefreshToken());
    }

    public async Task<string?> GetValidAccessTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(_inMemoryAccessToken) &&
            _accessTokenExpiresAtUtc.HasValue &&
            _accessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return _inMemoryAccessToken;
        }

        await _tokenRefreshSemaphore.WaitAsync();
        try
        {
            if (!string.IsNullOrWhiteSpace(_inMemoryAccessToken) &&
                _accessTokenExpiresAtUtc.HasValue &&
                _accessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return _inMemoryAccessToken;
            }

            _inMemoryAccessToken = null;
            _accessTokenExpiresAtUtc = null;

            var refreshToken = _secureStorage.RetrieveSecret(RefreshTokenKey);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                _logger.LogWarning("Refresh Token not found. A login is required via the module settings.");
                return null;
            }

            _logger.LogInfo("Access token expired or not found. Refreshing...");

            try
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", refreshToken },
                    { "client_id", SpotifyClientId }
                });

                var responseJson = await _authClient.PostStringAsync("https://accounts.spotify.com/api/token",
                    await content.ReadAsStringAsync(),
                    Encoding.UTF8, "application/x-www-form-urlencoded");

                using var jsonDoc = JsonDocument.Parse(responseJson);
                var newAccessToken = jsonDoc.RootElement.GetProperty("access_token").GetString();
                var expiresInSeconds = jsonDoc.RootElement.TryGetProperty("expires_in", out var expiresInElement)
                    ? expiresInElement.GetInt32()
                    : 3600;

                if (jsonDoc.RootElement.TryGetProperty("refresh_token", out var newRefreshTokenElement))
                {
                    var newRefreshToken = newRefreshTokenElement.GetString();
                    if (!string.IsNullOrWhiteSpace(newRefreshToken))
                    {
                        _logger.LogInfo("Spotify provided a new rotated refresh token. Updating secure storage.");
                        _secureStorage.StoreSecret(RefreshTokenKey, newRefreshToken);
                    }
                }

                if (string.IsNullOrWhiteSpace(newAccessToken))
                {
                    _logger.LogError(null,
                        "Failed to refresh token: API response did not contain an access_token.");
                    return null;
                }

                _logger.LogInfo("Successfully refreshed access token.");
                _inMemoryAccessToken = newAccessToken;

                // Never cache beyond the server's reported expiry; keep a safety margin.
                const int safetyMarginSeconds = 30;
                var effectiveExpirySeconds = expiresInSeconds > safetyMarginSeconds
                    ? expiresInSeconds - safetyMarginSeconds
                    : Math.Max(5, expiresInSeconds - 5);

                _accessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(effectiveExpirySeconds);
                UpdateUiForAuthenticationState(true);
                return _inMemoryAccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to refresh token. The refresh token might be invalid or a network error occurred. Please login again if the problem persists.");
                return null;
            }
        }
        finally
        {
            try
            {
                _tokenRefreshSemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // Ignore if disposed during shutdown
            }
        }
    }

    private void UpdateUiForAuthenticationState(bool isAuthenticated)
    {
        if (isAuthenticated)
        {
            _settings.AuthStatus.SetValue("Authenticated");
            _loginAction.SetLabel("Re-Login with Spotify");
        }
        else
        {
            _settings.AuthStatus.SetValue("Login required");
            _loginAction.SetLabel("Login to Spotify");
        }

        _logoutAction.SetEnabled(isAuthenticated);
        _settings.CustomUrl.SetVisibility(
            _settings.PlaybackContext.GetCurrentValue() == Settings.CustomUrlValue);

        AuthenticationStateChanged?.Invoke(isAuthenticated);
    }

    private void Logout()
    {
        _secureStorage.DeleteSecret(RefreshTokenKey);
        _inMemoryAccessToken = null;
        _accessTokenExpiresAtUtc = null;
        _logger.LogInfo("Logged out from Spotify");
    }

    private async Task<bool> PerformPkceLoginAsync()
    {
        var (codeVerifier, codeChallenge) = GeneratePkcePair();

        HttpListener? listener = null;
        string? redirectUri = null;

        foreach (var port in AllowedPorts)
        {
            try
            {
                var uri = $"http://127.0.0.1:{port}{RedirectPath}";
                var tempListener = new HttpListener();
                tempListener.Prefixes.Add(uri);
                tempListener.Start();

                listener = tempListener;
                redirectUri = uri;
                _logger.LogInfo("Successfully bound local listener to {Uri}", uri);
                break;
            }
            catch (HttpListenerException)
            {
                _logger.LogDebug("Port {Port} is busy, trying next...", port);
            }
        }

        // Fallback: try a handful of random dynamic ports (IANA range). This may still fail
        // if the redirect URI is not pre-registered, but preserves previous behavior.
        if (listener == null)
        {
            var rnd = new Random();
            const int maxAttempts = 8;
            for (var i = 0; i < maxAttempts; i++)
            {
                HttpListener? tempListener = null;
                try
                {
                    var port = rnd.Next(49152, 65535);
                    var uri = $"http://127.0.0.1:{port}{RedirectPath}";
                    tempListener = new HttpListener();
                    tempListener.Prefixes.Add(uri);
                    tempListener.Start();

                    listener = tempListener;
                    redirectUri = uri;
                    _logger.LogWarning(
                        "All preferred ports busy. Using dynamic port {Port}. Ensure this port is registered in Spotify redirect URIs.",
                        port);
                    break;
                }
                catch (HttpListenerException)
                {
                    tempListener?.Close();
                }
                catch (Exception ex)
                {
                    tempListener?.Close();
                    _logger.LogError(ex, "Failed to bind to dynamic port for OAuth callback attempt {Attempt}", i + 1);
                }
            }
        }

        if (listener == null || redirectUri == null)
        {
            var msg =
                $"Failed to bind any port for OAuth callback. Tried preferred ports {AllowedPorts[0]}-{AllowedPorts[^1]} and dynamic fallback.";
            _logger.LogError(null, msg);
            _notifier.ShowToast(
                "Error: Could not bind local port for Spotify login. Free port 8888-8895 or allow a dynamic port and retry.",
                NotificationType.Error);
            return false;
        }

        using (listener)
        {
            var scopes =
                "user-modify-playback-state user-read-playback-state user-read-private playlist-read-private user-library-read";
            var authUrl = $"https://accounts.spotify.com/authorize?client_id={SpotifyClientId}" +
                          "&response_type=code" +
                          $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                          $"&scope={Uri.EscapeDataString(scopes)}" +
                          "&code_challenge_method=S256" +
                          $"&code_challenge={codeChallenge}";

            try
            {
                _logger.LogInfo("Attempting to open browser for Spotify login.");
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                _notifier.ShowToast("Your browser has been opened to log in to Spotify. Please grant access.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open browser automatically.");
                _notifier.ShowToast(
                    $"Failed to open browser automatically. Please copy and paste this URL into your browser: {authUrl}",
                    NotificationType.Error);
                return false;
            }

            _notifier.ShowToast("Waiting for Spotify authorization in browser...");

            var contextTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));

            var completedTask = await Task.WhenAny(contextTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Spotify authentication timed out after 5 minutes");
                _notifier.ShowToast("Error: Authentication timed out", NotificationType.Error);
                return false;
            }

            var context = await contextTask.ConfigureAwait(false);
            var code = context.Request.QueryString.Get("code");
            var error = context.Request.QueryString.Get("error");

            var response = context.Response;
            var responseString = string.IsNullOrEmpty(error)
                ? "<html><body style='background:#121212;color:#e0e0e0;font-family:sans-serif;text-align:center;padding-top:50px;'><h1>Axorith Connected!</h1><p>You can now close this tab and return to the app.</p></body></html>"
                : $"<html><body style='background:#121212;color:#ff5555;font-family:sans-serif;text-align:center;padding-top:50px;'><h1>Login Failed</h1><p>Spotify returned error: {error}</p></body></html>";

            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            response.OutputStream.Close();

            if (string.IsNullOrWhiteSpace(code))
            {
                _logger.LogError(null, "Spotify login failed or was denied by user. Error: {Error}",
                    error ?? "Unknown error");
                _notifier.ShowToast($"Error: {error ?? "Unknown error"}", NotificationType.Error);
                return false;
            }

            try
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" },
                    { "code", code },
                    { "redirect_uri", redirectUri },
                    { "client_id", SpotifyClientId },
                    { "code_verifier", codeVerifier }
                });

                var responseJson = await _authClient.PostStringAsync("https://accounts.spotify.com/api/token",
                    await content.ReadAsStringAsync(),
                    Encoding.UTF8, "application/x-www-form-urlencoded");

                using var jsonDoc = JsonDocument.Parse(responseJson);
                var refreshToken = jsonDoc.RootElement.GetProperty("refresh_token").GetString();

                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    _logger.LogError(null, "Failed to retrieve refresh token from Spotify response.");
                    return false;
                }

                _secureStorage.StoreSecret(RefreshTokenKey, refreshToken);
                _logger.LogInfo("SUCCESS: New Refresh Token has been obtained and saved securely.");
                _settings.AuthStatus.SetValue("Authenticated");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to exchange authorization code for tokens.");
                _notifier.ShowToast("Error: Token exchange failed.", NotificationType.Error);
                return false;
            }
        }
    }

    private static (string, string) GeneratePkcePair()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var codeVerifier = Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        var codeChallenge = Convert.ToBase64String(challengeBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (codeVerifier, codeChallenge);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        // Intentionally do not dispose _tokenRefreshSemaphore to avoid race with in-flight refresh tasks
    }
}