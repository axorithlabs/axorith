using System.Text;

namespace Axorith.Shared.Utils;

public static class DeviceIdProvider
{
    private const string MutexName = "Axorith.DeviceInstallationIdentity";

    public static string GetDeviceId()
    {
        if (OperatingSystem.IsWindows())
        {
            var machineIdPath = Path.Combine(ApplicationPaths.MachineConfig, "installation-id.txt");
            if (Guid.TryParse(File.Exists(machineIdPath) ? File.ReadAllText(machineIdPath) : null, out var machineId))
            {
                return machineId.ToString("D");
            }
        }

        return GetDeviceId(Path.Combine(ApplicationPaths.Config, "installation-id.txt"));
    }

    internal static string GetDeviceId(string idFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idFilePath);
        var path = Path.GetFullPath(idFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var mutex = new Mutex(false, MutexName);
        try
        {
            try
            {
                mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                // The previous process exited while holding the identity lock.
            }

            if (Guid.TryParse(File.Exists(path) ? File.ReadAllText(path) : null, out var existingId))
            {
                return existingId.ToString("D");
            }

            var installationId = Guid.NewGuid().ToString("D");
            File.WriteAllText(path, installationId, new UTF8Encoding(false));
            return installationId;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
