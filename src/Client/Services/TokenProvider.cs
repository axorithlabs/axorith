using Axorith.Client.Services.Abstractions;
using Axorith.Contracts;
using Axorith.Shared.Utils;
using Axorith.Telemetry;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

public class FileTokenProvider(ILogger<FileTokenProvider> logger) : ITokenProvider
{
    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        var tokenPath = Path.Combine(ApplicationPaths.Config, AuthConstants.TokenFileName);

        // Extended retry logic: Host initialization can take 5-10 seconds
        // We try for up to 12 seconds (60 * 200ms) to accommodate slower systems
        var maxAttempts = 60;
        var delayMs = 200;
        var lastLogTime = DateTime.UtcNow;

        for (var i = 0; i < maxAttempts; i++)
        {
            // Log progress every 3 seconds
            if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 3)
            {
                logger.LogInformation("Waiting for auth token... (attempt {Attempt}/{Max}, elapsed {Elapsed}s)",
                    i + 1, maxAttempts, i * delayMs / 1000);
                lastLogTime = DateTime.UtcNow;
            }

            if (File.Exists(tokenPath))
            {
                try
                {
                    using var fs = new FileStream(tokenPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    var token = await reader.ReadToEndAsync(ct);

                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        logger.LogInformation("Auth token loaded successfully after {Elapsed}s", i * delayMs / 1000);
                        return token.Trim();
                    }

                    logger.LogDebug("Token file exists but is empty, waiting for Host to write...");
                }
                catch (IOException ioEx)
                {
                    // File might be locked by Host writing it
                    logger.LogDebug(ioEx, "Token file locked, retrying...");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to read token file, retrying...");
                }
            }

            await Task.Delay(delayMs, ct);
        }

        logger.LogError("Auth token file not found at {Path} after {Timeout}s. Host may have failed to start.",
            TelemetryGuard.SafePath(tokenPath), maxAttempts * delayMs / 1000);
        return null;
    }
}