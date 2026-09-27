using System.Security.AccessControl;
using System.Security.Principal;
using Axorith.Core.Services;

#pragma warning disable CA1416

namespace Axorith.Host.Services;

public static class CommitmentStatePaths
{
    private static readonly string[] StateFiles = ["committed-session.json", "strict-protection.json"];

    public static string Resolve(string fallbackDirectory, bool secure = true, bool migrateLegacy = true)
    {
        if (!OperatingSystem.IsWindows())
        {
            return fallbackDirectory;
        }

        if (!secure)
        {
            if (migrateLegacy)
            {
                MigrateLegacyStateInPlace(fallbackDirectory);
            }

            return fallbackDirectory;
        }

        var directory = GetProtectedDirectory();
        if (migrateLegacy)
        {
            MigrateLegacyState(fallbackDirectory, directory);
        }

        return directory;
    }

    private static string GetProtectedDirectory()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
                  ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var appRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Axorith");
        SecureDirectory(appRoot);
        var root = Path.Combine(appRoot, "CommittedSessions");
        SecureDirectory(root);
        var directory = Path.Combine(root, sid);
        SecureDirectory(directory);
        return directory;
    }

    private static void MigrateLegacyStateInPlace(string directory)
    {
        foreach (var name in StateFiles)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var payload = CommittedSessionStateFile.ReadPayloadOrLegacy(path);
            if (payload == File.ReadAllText(path))
            {
                CommittedSessionStateFile.WritePayload(path, payload);
            }
        }
    }

    private static void MigrateLegacyState(string oldDirectory, string newDirectory)
    {
        foreach (var name in StateFiles)
        {
            var oldPath = Path.Combine(oldDirectory, name);
            var newPath = Path.Combine(newDirectory, name);
            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                CommittedSessionStateFile.WritePayload(newPath,
                    CommittedSessionStateFile.ReadPayloadOrLegacy(oldPath));
            }
        }

        foreach (var name in StateFiles)
        {
            var oldPath = Path.Combine(oldDirectory, name);
            if (File.Exists(oldPath) && File.Exists(Path.Combine(newDirectory, name)))
            {
                File.Delete(oldPath);
                var tempPath = oldPath + ".tmp";
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }

    private static void SecureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var directory = new DirectoryInfo(path);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Committed session storage cannot use a reparse point: '{path}'.");
        }

        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(administrators);
        foreach (var sid in new[] { administrators, system })
        {
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }

        directory.SetAccessControl(security);
    }
}
