using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Axorith.Shared.Platform;
using Axorith.Shared.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axorith.Shim;

internal static class Program
{
    private const int MaxLogSizeBytes = 10 * 1024 * 1024;
    private const int LogFlushIntervalMs = 5000;
    private const int MaxNativeMessageBytes = 1024 * 1024;

    private static readonly ConcurrentQueue<string> LogQueue = new();
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> PendingResponses = new();
    private static readonly SemaphoreSlim LogSemaphore = new(1, 1);
    private static CancellationTokenSource? _logFlushCts;
    private static Task? _logFlushTask;

    public static async Task Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--remove-legacy-firefox-policy",
                StringComparison.OrdinalIgnoreCase))
        {
            PlatformServices.CreateNativeMessagingManager(NullLoggerFactory.Instance)
                .RemoveFirefoxExtensionPolicy(SiteBlockerExtensionIds.Firefox);
            return;
        }

        StartLogFlusher();

        var loggerFactory = NullLoggerFactory.Instance;
        var pipeFactory = PlatformServices.CreateNamedPipeFactory(loggerFactory);
        var pipeName = GetPipeName(args);
        using var shutdown = new CancellationTokenSource();
        var inputMonitor = MonitorStandardInputAsync(shutdown);

        try
        {
            await ListenForExtensionAsync(pipeFactory, pipeName, shutdown.Token).ConfigureAwait(false);
        }
        finally
        {
            shutdown.Cancel();
            await inputMonitor.ConfigureAwait(false);
            await StopLogFlusherAsync().ConfigureAwait(false);
        }
    }

    private static async Task ListenForExtensionAsync(INamedPipeFactory pipeFactory, string pipeName,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = pipeFactory.CreateSecureServerPipe(pipeName, PipeDirection.InOut, 16);
                await pipeServer.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(pipeServer, new UTF8Encoding(false), false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipeServer, new UTF8Encoding(false), 1024, leaveOpen: true)
                {
                    AutoFlush = true
                };
                var message = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(message))
                {
                    var requestId = GetRequestId(message);
                    if (string.IsNullOrEmpty(requestId))
                    {
                        SendMessageToExtension(message);
                        continue;
                    }

                    var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (!PendingResponses.TryAdd(requestId, pending))
                    {
                        await writer.WriteLineAsync(CreateErrorResponse(requestId, "Error", "Duplicate request id."));
                        continue;
                    }

                    try
                    {
                        SendMessageToExtension(message);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        try
                        {
                            var response = await pending.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                            await writer.WriteLineAsync(response.AsMemory(), ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            await writer.WriteLineAsync(CreateErrorResponse(requestId, "Outdated",
                                "The extension did not respond to the health protocol.").AsMemory(), ct)
                                .ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        PendingResponses.TryRemove(requestId, out _);
                    }
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
            var input = Console.OpenStandardInput();
            var header = new byte[sizeof(int)];
            while (!shutdown.IsCancellationRequested)
            {
                var firstRead = await input.ReadAsync(header.AsMemory(0, header.Length), shutdown.Token)
                    .ConfigureAwait(false);
                if (firstRead == 0)
                {
                    break;
                }

                if (firstRead < header.Length)
                {
                    await input.ReadExactlyAsync(header.AsMemory(firstRead), shutdown.Token).ConfigureAwait(false);
                }

                var messageLength = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (messageLength <= 0 || messageLength > MaxNativeMessageBytes)
                {
                    throw new InvalidDataException($"Invalid native message length: {messageLength}.");
                }

                var messageBytes = new byte[messageLength];
                await input.ReadExactlyAsync(messageBytes, shutdown.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(messageBytes);
                if (document.RootElement.TryGetProperty("requestId", out var requestIdElement) &&
                    requestIdElement.ValueKind == JsonValueKind.String &&
                    PendingResponses.TryGetValue(requestIdElement.GetString()!, out var pending))
                {
                    pending.TrySetResult(Encoding.UTF8.GetString(messageBytes));
                }
            }
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

    private static string GetPipeName(string[] args)
    {
        var browser = args.FirstOrDefault(arg => arg.StartsWith("--browser=", StringComparison.OrdinalIgnoreCase))?
            .Split('=', 2)[1].ToLowerInvariant();
        if (browser is "chrome" or "edge" or "chromium" or "firefox")
        {
            return $"axorith-nm-pipe-{browser}";
        }

        // Firefox passes the add-on ID as an argument; manifest "args" are not passed to the host.
        return args.Any(arg => string.Equals(arg, SiteBlockerExtensionIds.Firefox, StringComparison.OrdinalIgnoreCase))
            ? "axorith-nm-pipe-firefox"
            : "axorith-nm-pipe";
    }

    private static string? GetRequestId(string message)
    {
        using var document = JsonDocument.Parse(message);
        return document.RootElement.TryGetProperty("requestId", out var requestId) &&
               requestId.ValueKind == JsonValueKind.String
            ? requestId.GetString()
            : null;
    }

    private static string CreateErrorResponse(string requestId, string status, string message) =>
        JsonSerializer.Serialize(new { requestId, protocolVersion = 1, ok = false, status, message });

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
