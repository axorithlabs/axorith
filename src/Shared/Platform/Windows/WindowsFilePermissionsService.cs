using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal class WindowsFilePermissionsService(ILogger<WindowsFilePermissionsService> logger) : IFilePermissionsService
{
    public void SetRestrictivePermissions(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileSecurity = fileInfo.GetAccessControl();

            fileSecurity.SetAccessRuleProtection(true, false);

            var currentUser = WindowsIdentity.GetCurrent();
            var currentUserSid = currentUser.User;

            if (currentUserSid != null)
            {
                var userRule = new FileSystemAccessRule(
                    currentUserSid,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow);

                fileSecurity.AddAccessRule(userRule);
            }

            fileInfo.SetAccessControl(fileSecurity);
            logger.LogDebug("Set restrictive permissions on file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set Windows file permissions on {FilePath}", filePath);
        }
    }
}