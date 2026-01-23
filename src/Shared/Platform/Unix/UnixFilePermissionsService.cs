using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Unix;

[UnsupportedOSPlatform("windows")]
internal class UnixFilePermissionsService(ILogger<UnixFilePermissionsService> logger) : IFilePermissionsService
{
    public void SetRestrictivePermissions(string filePath)
    {
        try
        {
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            logger.LogDebug("Set restrictive permissions on file: {FilePath} (0600)", filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set Unix file permissions on {FilePath}", filePath);
        }
    }
}
