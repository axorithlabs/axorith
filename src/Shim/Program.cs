using System.Text;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shim;

internal static class Program
{
    private const string PipeName = "axorith-nm-pipe";
    private const int MaxLogSizeBytes = 10 * 1024 * 1024; // 10 MB

    public static void Main()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var pipeFactory = PlatformServices.CreateNamedPipeFactory(loggerFactory);

        while (true)
            try
            {
                using var pipeServer = pipeFactory.CreateSecureServerPipe(PipeName);
                pipeServer.WaitForConnection();

                using var reader = new StreamReader(pipeServer);

                var message = reader.ReadToEnd();

                if (!string.IsNullOrWhiteSpace(message))
                {
                    SendMessageToExtension(message);
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
    }

    private static void SendMessageToExtension(string jsonMessage)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(jsonMessage);
            var lengthBytes = BitConverter.GetBytes(bytes.Length);

            using var stdout = Console.OpenStandardOutput();
            stdout.Write(lengthBytes, 0, 4);
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }
        catch (Exception ex)
        {
            LogException(ex, "Failed to send message to extension");
        }
    }

    private static void LogException(Exception ex, string? context = null)
    {
        try
        {
            var logsDir = ApplicationPaths.EnsureDirectoryExists(ApplicationPaths.Logs);
            var errorLogPath = Path.Combine(logsDir, "shim_error.log");

            var fileInfo = new FileInfo(errorLogPath);
            if (fileInfo is { Exists: true, Length: > MaxLogSizeBytes })
            {
                var archivePath = Path.Combine(logsDir, $"shim_error_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.Move(errorLogPath, archivePath);
            }

            var errorMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " +
                               $"{(context != null ? $"[{context}] " : "")}" +
                               $"{ex.GetType().Name}: {ex.Message}\n" +
                               $"StackTrace: {ex.StackTrace}\n\n";

            File.AppendAllText(errorLogPath, errorMessage);
        }
        catch
        {
            // If logging fails, there is nothing more we can do.
        }
    }
}
