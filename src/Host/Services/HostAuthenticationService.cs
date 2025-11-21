using System.Security.Cryptography;
using Axorith.Contracts;
using Microsoft.Extensions.Options;

namespace Axorith.Host.Services;

public interface IHostAuthenticationService
{
    void InitializeToken();
    bool ValidateToken(string? token);
}

public class HostAuthenticationService(
    IOptions<Configuration> config,
    ILogger<HostAuthenticationService> logger) : IHostAuthenticationService
{
    private string _currentToken = string.Empty;

    public void InitializeToken()
    {
        var presetsPath = config.Value.Persistence.ResolvePresetsPath();
        var appDataDir = Directory.GetParent(presetsPath)?.FullName
                         ?? Path.GetDirectoryName(presetsPath)!;

        var tokenFilePath = Path.Combine(appDataDir, AuthConstants.TokenFileName);

        try
        {
            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }

            if (File.Exists(tokenFilePath))
            {
                try
                {
                    var existingToken = File.ReadAllText(tokenFilePath).Trim();
                    if (!string.IsNullOrWhiteSpace(existingToken))
                    {
                        Convert.FromBase64String(existingToken);
                        _currentToken = existingToken;
                        logger.LogInformation("Loaded existing auth token from {Path}", tokenFilePath);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Existing token file is invalid or unreadable. Generating new one.");
                }
            }

            var tokenData = RandomNumberGenerator.GetBytes(32);
            _currentToken = Convert.ToBase64String(tokenData);

            File.WriteAllText(tokenFilePath, _currentToken);
            logger.LogInformation("New auth token generated and written to {Path}", tokenFilePath);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to initialize auth token at {Path}", tokenFilePath);
            throw;
        }
    }

    public bool ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_currentToken))
        {
            return false;
        }

        try
        {
            var tokenBytes = Convert.FromBase64String(token);
            var currentBytes = Convert.FromBase64String(_currentToken);
            return CryptographicOperations.FixedTimeEquals(tokenBytes, currentBytes);
        }
        catch
        {
            return false;
        }
    }
}