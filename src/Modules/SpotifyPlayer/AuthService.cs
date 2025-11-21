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
    private readonly CompositeDisposable _disposables = [];
    private readonly SemaphoreSlim _tokenRefreshSemaphore = new(1, 1);

    private string? _inMemoryAccessToken;

    private const string RefreshTokenKey = "SpotifyRefreshToken";
    private const string SpotifyClientId = "b9335aa114364ba8b957b44d33bb735d";
    private const string RedirectHost = "http://127.0.0.1";
    private const string RedirectPath = "/callback/";

    private readonly Action _loginAction;
    private readonly Action _logoutAction;
    private readonly Action _updateAction;

    public event Action<bool>? AuthenticationStateChanged;

    public AuthService(IModuleLogger logger, IHttpClientFactory httpClientFactory,
        ISecureStorageService secureStorage, ModuleDefinition definition, Settings settings)
    {
        _logger = logger;
        _secureStorage = secureStorage;
        _settings = settings;
        _authClient = httpClientFactory.CreateClient($"{definition.Name}.Auth");

        _loginAction = _settings.LoginAction;
        _logoutAction = _settings.LogoutAction;
        _updateAction = _settings.UpdateAction;

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
        if (!string.IsNullOrWhiteSpace(_inMemoryAccessToken)) return _inMemoryAccessToken;

        await _tokenRefreshSemaphore.WaitAsync();
        try
        {
            if (!string.IsNullOrWhiteSpace(_inMemoryAccessToken)) return _inMemoryAccessToken;

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
            _tokenRefreshSemaphore.Release();
        }
    }

    private void UpdateUiForAuthenticationState(bool isAuthenticated)
    {
        if (isAuthenticated)
        {
            _settings.AuthStatus.SetValue("Authenticated ✓");
            _loginAction.SetLabel("Re-Login with Spotify");
        }
        else
        {
            _settings.AuthStatus.SetValue("⚠ Login required");
            _loginAction.SetLabel("Login to Spotify");
        }

        _logoutAction.SetEnabled(isAuthenticated);
        _updateAction.SetEnabled(isAuthenticated);
        _settings.CustomUrl.SetVisibility(
            _settings.PlaybackContext.GetCurrentValue() == Settings.CustomUrlValue);

        AuthenticationStateChanged?.Invoke(isAuthenticated);
    }

    private void Logout()
    {
        _secureStorage.DeleteSecret(RefreshTokenKey);
        _inMemoryAccessToken = null;
        _logger.LogInfo("Logged out from Spotify");
    }

    private async Task<bool> PerformPkceLoginAsync()
    {
        var (codeVerifier, codeChallenge) = GeneratePkcePair();
        var redirectUri = GetRedirectUri();

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);

        try
        {
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                var port = GetConfiguredRedirectPort();
                _logger.LogError(ex,
                    "Failed to start local HTTP listener on {RedirectUri}. Port {Port} might be in use or blocked.",
                    redirectUri, port);
                _settings.AuthStatus.SetValue(
                    $"Error: Redirect port {port} is in use or blocked. Change 'Redirect Port' setting and try again.");
                return false;
            }

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
                _logger.LogWarning("Your browser has been opened to log in to Spotify. Please grant access.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to open browser automatically. Please copy and paste this URL into your browser: {Url}",
                    authUrl);
                _settings.AuthStatus.SetValue("Error: Could not open browser. See logs.");
                return false;
            }

            _settings.AuthStatus.SetValue("Waiting for Spotify authorization in browser...");

            var contextTask = listener.GetContextAsync();

            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
            var completedTask = await Task.WhenAny(contextTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Spotify authentication timed out after 5 minutes");
                _settings.AuthStatus.SetValue("Error: Authentication timed out");

                listener.Stop();
                return false;
            }

            var context = await contextTask.ConfigureAwait(false);

            var code = context.Request.QueryString.Get("code");

            var response = context.Response;
            var buffer = "<html><body><h1>Success!</h1><p>You can now close this browser tab.</p></body></html>"u8
                .ToArray();
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            response.OutputStream.Close();
            listener.Stop();

            if (string.IsNullOrWhiteSpace(code)) return false;

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

            if (string.IsNullOrWhiteSpace(refreshToken)) return false;

            _secureStorage.StoreSecret(RefreshTokenKey, refreshToken);
            _logger.LogInfo("SUCCESS: New Refresh Token has been obtained and saved securely.");
            _settings.AuthStatus.SetValue("Authenticated ✓");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during PKCE login process.");
            _settings.AuthStatus.SetValue("Error: Login failed. See logs for details.");
            return false;
        }
        finally
        {
            if (listener.IsListening) listener.Stop();
        }
    }

    private string GetRedirectUri()
    {
        var port = GetConfiguredRedirectPort();
        return $"{RedirectHost}:{port}{RedirectPath}";
    }

    private int GetConfiguredRedirectPort()
    {
        return _settings.RedirectPort.GetCurrentValue();
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
        _tokenRefreshSemaphore.Dispose();
    }
}