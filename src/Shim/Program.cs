using System.IO.Pipes;
using System.Text;
using Axorith.Shared.Utils;

namespace Axorith.Shim;

internal static class Program
{
    private const string PipeName = "axorith-nm-pipe";

    public static void Main()
    {
        while (true)
            try
            {
                using var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

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
                // In case of an error (e.g., pipe issues), log it for debugging.
                // IMPORTANT: Do NOT write to Console.Error or Console.WriteLine, as it will corrupt the native messaging channel.
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