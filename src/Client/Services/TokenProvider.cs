using Axorith.Client.Services.Abstractions;
using Axorith.Contracts;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

public class FileTokenProvider(ILogger<FileTokenProvider> logger) : ITokenProvider
{
    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var tokenPath = Path.Combine(appData, "Axorith", AuthConstants.TokenFileName);

        // Retry logic: Host might be starting up and hasn't written the file yet.
        // We try for 5 seconds (10 * 500ms).
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(tokenPath))
            {
                try
                {
                    // Use FileShare.ReadWrite to avoid locking issues if Host is writing
                    using var fs = new FileStream(tokenPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    var token = await reader.ReadToEndAsync(ct);

                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        return token.Trim();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to read token file, retrying...");
                }
            }

            await Task.Delay(500, ct);
        }

        logger.LogError("Auth token file not found at {Path} after retries. Ensure Host is running.", tokenPath);
        return null;
    }
}