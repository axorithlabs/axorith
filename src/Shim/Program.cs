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
    private static Task? _logFlushTask;

    public static async Task Main()
    {
        StartLogFlusher();

        var loggerFactory = NullLoggerFactory.Instance;
        var pipeFactory = PlatformServices.CreateNamedPipeFactory(loggerFactory);
        using var shutdown = new CancellationTokenSource();
        var inputMonitor = MonitorStandardInputAsync(shutdown);

        try
        {
            await ListenForExtensionAsync(pipeFactory, shutdown.Token).ConfigureAwait(false);
        }
        finally
        {
            shutdown.Cancel();
            await inputMonitor.ConfigureAwait(false);
            await StopLogFlusherAsync().ConfigureAwait(false);
        }
    }

    private static async Task ListenForExtensionAsync(INamedPipeFactory pipeFactory, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = pipeFactory.CreateSecureServerPipe(PipeName);
                await pipeServer.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(pipeServer);
                var message = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(message))
                {
                    SendMessageToExtension(message);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                await LogExceptionAsync(ex).ConfigureAwait(false);
            }
        }
    }

    private static async Task MonitorStandardInputAsync(CancellationTokenSource shutdown)
    {
        try
        {
            await Console.OpenStandardInput().CopyToAsync(Stream.Null, shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Expected when the named pipe loop ends.
        }
        catch (Exception ex)
        {
            await LogExceptionAsync(ex, "Native messaging input closed with an error").ConfigureAwait(false);
        }
        finally
        {
            shutdown.Cancel();
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

        _logFlushTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(LogFlushIntervalMs));

            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    await FlushLogsAsync();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Expected during shutdown.
            }
        });
    }

    private static async Task StopLogFlusherAsync()
    {
        if (_logFlushCts == null)
        {
            return;
        }

        _logFlushCts.Cancel();
        if (_logFlushTask != null)
        {
            await _logFlushTask.ConfigureAwait(false);
        }

        await FlushLogsAsync().ConfigureAwait(false);
        _logFlushCts.Dispose();
        _logFlushCts = null;
        _logFlushTask = null;
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
