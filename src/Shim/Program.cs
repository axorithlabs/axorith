using System.IO.Pipes;
using System.Text;

namespace Axorith.Shim;

/// <summary>
///     A lightweight, dependency-free shim that acts as a bridge for Native Messaging.
///     It runs as a background process initiated by the browser, hosts a Named Pipe server,
///     listens for messages from the main Axorith application, and relays them to the browser extension via stdout.
/// </summary>
internal static class Program
{
    private const string PipeName = "axorith-nm-pipe";

    /// <summary>
    ///     The main entry point for the shim application.
    /// </summary>
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

    /// <summary>
    ///     Sends a message to the browser extension by writing to the standard output stream
    ///     according to the Native Messaging protocol (32-bit length prefix + UTF-8 JSON payload).
    /// </summary>
    /// <param name="jsonMessage">The JSON message string to send.</param>
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

    /// <summary>
    ///     Logs an exception to a 'shim_error.log' file located next to the executable.
    /// </summary>
    private static void LogException(Exception ex, string? context = null)
    {
        try
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logsDir = Path.Combine(appDataPath, "Axorith", "logs");
            var errorLogPath = Path.Combine(logsDir, "shim_error.log");

            Directory.CreateDirectory(logsDir);

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