using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Windows;

[SupportedOSPlatform("windows")]
internal class WindowsNamedPipeFactory(ILogger<WindowsNamedPipeFactory> logger) : INamedPipeFactory
{
    public NamedPipeServerStream CreateSecureServerPipe(
        string pipeName,
        PipeDirection direction = PipeDirection.In,
        int maxNumberOfServerInstances = 1)
    {
        var pipeSecurity = new PipeSecurity();

        try
        {
            var currentUser = WindowsIdentity.GetCurrent();
            var currentUserSid = currentUser.User;

            if (currentUserSid != null)
            {
                var userRule = new PipeAccessRule(
                    currentUserSid,
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow);

                pipeSecurity.AddAccessRule(userRule);
            }

            pipeSecurity.SetAccessRuleProtection(true, false);

            logger.LogDebug("Created secure named pipe with ACL: {PipeName}", pipeName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set pipe security, using default");
        }

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            direction,
            maxNumberOfServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            4096,
            0,
            pipeSecurity);
    }
}