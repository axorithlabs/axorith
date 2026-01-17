using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

/// <summary>
///     Manages single instance enforcement for the Axorith Client.
///     Uses a named mutex to detect if another instance is running,
///     and named pipes for inter-process communication to activate the existing window.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private const string ActivateCommand = "ACTIVATE_WINDOW";
    
    private readonly ILogger<SingleInstanceManager> _logger;
    private Mutex? _mutex;
    private bool _isFirstInstance;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _pipeCts;
    private Task? _pipeListenerTask;

    public event EventHandler? ActivationRequested;

    public SingleInstanceManager(ILogger<SingleInstanceManager> logger, string? instanceId = null)
    {
        _logger = logger;
        var suffix = instanceId ?? "Default";
        _mutexName = $"Global\\AxorithClient_SingleInstance_Mutex_{suffix}";
        _pipeName = $"AxorithClient_SingleInstance_Pipe_{suffix}";
    }

    /// <summary>
    ///     Attempts to acquire the single instance lock.
    ///     Returns true if this is the first instance, false if another instance is already running.
    /// </summary>
    public bool TryAcquireLock()
    {
        try
        {
            // Try to create a new mutex - if it already exists, createdNew will be false
            bool createdNew;
            _mutex = new Mutex(true, _mutexName, out createdNew);

            if (createdNew)
            {
                // We created a new mutex, so we're the first instance
                _isFirstInstance = true;
                _logger.LogInformation("Single instance lock acquired - this is the first instance");
                StartPipeServer();
                return true;
            }

            // Mutex already exists, try to acquire it with zero timeout
            // If we can acquire it, the previous instance crashed
            if (_mutex.WaitOne(TimeSpan.Zero, false))
            {
                _logger.LogWarning("Acquired abandoned mutex - previous instance may have crashed");
                _isFirstInstance = true;
                StartPipeServer();
                return true;
            }

            // Another instance is running
            _logger.LogInformation("Another instance is already running");
            _mutex.Dispose();
            _mutex = null;
            _isFirstInstance = false;
            return false;
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed without releasing the mutex
            // We can safely take ownership
            _logger.LogWarning("Mutex was abandoned by previous instance - taking ownership");
            _isFirstInstance = true;
            StartPipeServer();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire single instance lock");
            // If we can't determine, assume we're the first instance to avoid blocking startup
            _isFirstInstance = true;
            return true;
        }
    }

    /// <summary>
    ///     Sends an activation request to the running instance.
    /// </summary>
    public async Task<bool> SendActivationRequestAsync()
    {
        try
        {
            _logger.LogInformation("Sending activation request to existing instance");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            
            await client.ConnectAsync(cts.Token);
            
            var message = Encoding.UTF8.GetBytes(ActivateCommand);
            await client.WriteAsync(message, cts.Token);
            await client.FlushAsync(cts.Token);

            _logger.LogInformation("Activation request sent successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send activation request to existing instance");
            return false;
        }
    }

    private void StartPipeServer()
    {
        try
        {
            _pipeCts = new CancellationTokenSource();
            _pipeListenerTask = Task.Run(() => ListenForActivationRequestsAsync(_pipeCts.Token));
            _logger.LogInformation("Named pipe server started for activation requests");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start named pipe server");
        }
    }

    private async Task ListenForActivationRequestsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _pipeServer = server;

                _logger.LogDebug("Waiting for activation request...");
                await server.WaitForConnectionAsync(ct);

                var buffer = new byte[1024];
                var bytesRead = await server.ReadAsync(buffer, ct);
                
                if (bytesRead > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    _logger.LogInformation("Received activation request: {Message}", message);

                    if (message == ActivateCommand)
                    {
                        ActivationRequested?.Invoke(this, EventArgs.Empty);
                    }
                }

                server.Disconnect();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Pipe listener cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in pipe listener");
                await Task.Delay(1000, ct); // Wait before retrying
            }
            finally
            {
                server?.Dispose();
            }
        }

        _logger.LogInformation("Named pipe server stopped");
    }

    public void Dispose()
    {
        try
        {
            _pipeCts?.Cancel();
            _pipeListenerTask?.Wait(TimeSpan.FromSeconds(2));
            _pipeServer?.Dispose();
            _pipeCts?.Dispose();

            if (_isFirstInstance && _mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _logger.LogInformation("Single instance lock released");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing SingleInstanceManager");
        }
    }
}
