using System.Collections.Concurrent;
using System.Text;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shim;

internal static class Program
{
    private const string PipeName = "axorith-nm-pipe";
    private const int MaxLogSizeBytes = 10 * 1024 * 1024;
    private const int LogFlushIntervalMs = 5000;

    private static readonly ConcurrentQueue<string> LogQueue = new();
    private static readonly SemaphoreSlim LogSemaphore = new(1, 1);
    private static CancellationTokenSource? _logFlushCts;

    public static void Main()
    {
        StartLogFlusher();

        var loggerFactory = NullLoggerFactory.Instance;
        var pipeFactory = PlatformServices.CreateNamedPipeFactory(loggerFactory);

        try
        {
            while (true)
            {
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
                    LogExceptionAsync(ex).GetAwaiter().GetResult();
                }
            }
        }
        finally
        {
            StopLogFlusher();
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
            _ = LogExceptionAsync(ex, "Failed to send message to extension");
        }
    }

    private static void StartLogFlusher()
    {
        _logFlushCts = new CancellationTokenSource();
        var token = _logFlushCts.Token;

        Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(LogFlushIntervalMs));

            while (await timer.WaitForNextTickAsync(token))
            {
                await FlushLogsAsync();
            }
        }, token);
    }

    private static void StopLogFlusher()
    {
        _logFlushCts?.Cancel();
        FlushLogsAsync().GetAwaiter().GetResult();
        _logFlushCts?.Dispose();
    }

    private static async Task FlushLogsAsync()
    {
        if (LogQueue.IsEmpty)
        {
            return;
        }

        await LogSemaphore.WaitAsync();
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

            var batch = new StringBuilder();
            while (LogQueue.TryDequeue(out var logEntry))
            {
                batch.AppendLine(logEntry);
            }

            if (batch.Length > 0)
            {
                await File.AppendAllTextAsync(errorLogPath, batch.ToString());
            }
        }
        catch
        {
            // If logging fails, clear queue to prevent memory leak
            while (LogQueue.TryDequeue(out _))
            {
            }
        }
        finally
        {
            LogSemaphore.Release();
        }
    }

    private static Task LogExceptionAsync(Exception ex, string? context = null)
    {
        var errorMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " +
                           $"{(context != null ? $"[{context}] " : "")}" +
                           $"{ex.GetType().Name}: {ex.Message}\n" +
                           $"StackTrace: {ex.StackTrace}\n";

        LogQueue.Enqueue(errorMessage);

        if (LogQueue.Count > 100)
        {
            return FlushLogsAsync();
        }

        return Task.CompletedTask;
    }
}