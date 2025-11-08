using System.Diagnostics;
using Axorith.Contracts;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Axorith.Client.Services;

/// <summary>
///     Manages Axorith.Host process lifecycle - auto-starts if not running.
/// </summary>
public class HostLauncher : IDisposable
{
    private readonly ILogger<HostLauncher> _logger;
    private Process? _hostProcess;
    private bool _disposed;

    public HostLauncher(ILogger<HostLauncher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Ensures Host is running, starting it if necessary.
    /// </summary>
    public async Task<bool> EnsureHostRunningAsync(string serverAddress = "http://127.0.0.1:5901",
        CancellationToken ct = default)
    {
        // Try to connect first (Host might already be running)
        if (await IsHostReachableAsync(serverAddress, ct))
        {
            _logger.LogInformation("Axorith.Host already running at {Address}", serverAddress);
            return true;
        }

        _logger.LogInformation("Axorith.Host not reachable, attempting to start...");

        return await StartHostProcessAsync(serverAddress, ct);
    }

    public async Task<bool> StartHostProcessAsync(string serverAddress = "http://127.0.0.1:5901",
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Looking for Host executable...");
            var hostExePath = FindHostExecutable();
            if (hostExePath == null)
            {
                _logger.LogError("Could not find Axorith.Host executable in any search path");
                return false;
            }

            _logger.LogInformation("Found Host at: {Path}", hostExePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = hostExePath,
                UseShellExecute = false,
                CreateNoWindow = true, // Hide console window
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _logger.LogInformation("Starting Host process...");
            var startedProcess = Process.Start(startInfo);
            if (startedProcess == null)
            {
                _logger.LogError("Failed to start Axorith.Host process");
                return false;
            }

            _hostProcess = startedProcess;

            _logger.LogInformation("Started Axorith.Host process (PID: {ProcessId})", startedProcess.Id);

            // Attach output handlers for logging
            startedProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogInformation("Host: {Output}", e.Data);
            };
            startedProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _logger.LogError("Host Error: {Error}", e.Data);
            };

            startedProcess.BeginOutputReadLine();
            startedProcess.BeginErrorReadLine();

            _logger.LogInformation("Waiting for Host to become ready...");

            // Give Host time to start gRPC server
            await Task.Delay(1000, ct);

            // Wait for Host to be ready (max 8 attempts = ~4 seconds)
            var maxAttempts = 8;
            var delay = 500; // 500ms per attempt

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                _logger.LogDebug("Checking Host readiness (attempt {Attempt}/{Max})...", attempt, maxAttempts);

                if (await IsHostReachableAsync(serverAddress, ct))
                {
                    _logger.LogInformation("Host is ready!");
                    return true;
                }

                if (attempt < maxAttempts)
                    await Task.Delay(delay, ct);
            }

            _logger.LogError("Host did not become ready within 3 seconds");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Axorith.Host");
            return false;
        }
    }

    public async Task<bool> IsHostReachableAsync(string serverAddress = "http://127.0.0.1:5901",
        CancellationToken ct = default)
    {
        try
        {
            // Try to create gRPC channel and call DiagnosticsService.GetHealth
            var channel = GrpcChannel.ForAddress(serverAddress, new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(1),
                    PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                    EnableMultipleHttp2Connections = true
                }
            });

            var diagnosticsClient = new DiagnosticsService.DiagnosticsServiceClient(channel);
            var response = await diagnosticsClient.GetHealthAsync(
                new HealthCheckRequest(),
                deadline: DateTime.UtcNow.AddSeconds(2),
                cancellationToken: ct);

            await channel.ShutdownAsync();
            return response.Status == HealthStatus.Healthy;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed");
            return false;
        }
    }

    public async Task<bool> RestartHostAsync(string serverAddress = "http://127.0.0.1:5901",
        CancellationToken ct = default)
    {
        _logger.LogInformation("Restarting Axorith.Host...");

        var stopped = await StopHostProcessAsync(ct);
        if (!stopped) _logger.LogWarning("Failed to stop Axorith.Host before restart");

        return await StartHostProcessAsync(serverAddress, ct);
    }

    private async Task<bool> StopHostProcessAsync(CancellationToken ct)
    {
        try
        {
            if (_hostProcess is { HasExited: false })
                try
                {
                    _logger.LogInformation("Stopping Axorith.Host process (PID: {ProcessId})", _hostProcess.Id);
                    _hostProcess.Kill(entireProcessTree: true);
                    #if NET8_0_OR_GREATER
                    await _hostProcess.WaitForExitAsync(ct);
                    #else
                    _hostProcess.WaitForExit();
                    #endif
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to stop Axorith.Host process (PID: {ProcessId})", _hostProcess.Id);
                }
                finally
                {
                    _hostProcess.Dispose();
                    _hostProcess = null;
                }

            var processes = Process.GetProcessesByName("Axorith.Host");
            foreach (var process in processes)
                try
                {
                    if (process.HasExited)
                    {
                        process.Dispose();
                        continue;
                    }

                    _logger.LogInformation("Stopping Axorith.Host process (PID: {ProcessId})", process.Id);
                    process.Kill(entireProcessTree: true);
                    #if NET8_0_OR_GREATER
                    await process.WaitForExitAsync(ct);
                    #else
                    process.WaitForExit();
                    #endif
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to stop Axorith.Host process (PID: {ProcessId})", process.Id);
                }
                finally
                {
                    process.Dispose();
                }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error while stopping Axorith.Host");
            return false;
        }
    }

    private string? FindHostExecutable()
    {
        // Try several locations
        var locations = new[]
        {
            // Same directory as Client
            Path.Combine(AppContext.BaseDirectory, "Axorith.Host.exe"),

            // Parent build directory
            Path.Combine(AppContext.BaseDirectory, "..", "Axorith.Host", "Axorith.Host.exe"),

            // Development build output
            Path.Combine(AppContext.BaseDirectory, "..", "..", "Host", "Axorith.Host.exe"),

            // Relative to project root during development
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Host", "bin", "Debug", "net9.0",
                "Axorith.Host.exe")
        };

        foreach (var location in locations)
        {
            var fullPath = Path.GetFullPath(location);
            if (File.Exists(fullPath))
            {
                _logger.LogDebug("Found Axorith.Host at: {Path}", fullPath);
                return fullPath;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_hostProcess != null)
            try
            {
                _hostProcess.CancelOutputRead();
                _hostProcess.CancelErrorRead();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _hostProcess.Dispose();
                _hostProcess = null;
            }

        GC.SuppressFinalize(this);
    }
}