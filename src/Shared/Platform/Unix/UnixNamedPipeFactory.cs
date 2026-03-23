using System.IO.Pipes;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Axorith.Shared.Platform.Unix;

[UnsupportedOSPlatform("windows")]
internal class UnixNamedPipeFactory(ILogger<UnixNamedPipeFactory> logger) : INamedPipeFactory
{
    public NamedPipeServerStream CreateSecureServerPipe(
        string pipeName,
        PipeDirection direction = PipeDirection.In,
        int maxNumberOfServerInstances = 1)
    {
        var socketDir = GetSecureSocketDirectory();
        var socketPath = Path.Combine(socketDir, $"CoreFxPipe_{pipeName}");

        var pipe = new NamedPipeServerStream(
            socketPath,
            direction,
            maxNumberOfServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        SetPipePermissions(socketPath);

        logger.LogDebug("Created named pipe: {PipeName} at {Path}", pipeName, socketPath);
        return pipe;
    }

    private string GetSecureSocketDirectory()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

        if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir))
        {
            var axorithDir = Path.Combine(runtimeDir, "axorith");
            EnsureSecureDirectory(axorithDir);
            return axorithDir;
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fallbackDir = Path.Combine(homeDir, ".local", "run", "axorith");
        EnsureSecureDirectory(fallbackDir);
        return fallbackDir;
    }

    private void EnsureSecureDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            File.SetUnixFileMode(dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set permissions on socket directory: {Dir}", dir);
        }
    }

    private void SetPipePermissions(string socketPath)
    {
        const int maxRetries = 10;

        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                if (!File.Exists(socketPath))
                {
                    Thread.Sleep(10);
                    continue;
                }

                File.GetUnixFileMode(socketPath);
                File.SetUnixFileMode(socketPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
                logger.LogDebug("Set permissions 600 on pipe socket: {Path}", socketPath);
                return;
            }
            catch (FileNotFoundException)
            {
                Thread.Sleep(10);
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                logger.LogDebug(ex, "Retry {Attempt}/{Max} setting permissions on {Path}",
                    i + 1, maxRetries, socketPath);
                Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to set permissions on named pipe socket after {Max} retries: {Path}. " +
                    "Other users on this system may be able to connect to the pipe.",
                    maxRetries, socketPath);
            }
        }
    }
}