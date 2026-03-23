using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Axorith.Client.Services.Abstractions;
using Axorith.Contracts;
using Axorith.Shared.Utils;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axorith.Client.Services;

public class HostController(
    IOptions<Configuration> config,
    ILogger<HostController> logger,
    ITokenProvider tokenProvider) : IHostController
{
    private static readonly string HostInfoPath = ApplicationPaths.HostInfoFile;

    private static readonly string HostStartMutexName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "Global\\AxorithHostStartMutex"
        : "AxorithHostStartMutex";

    private static readonly Mutex HostStartMutex = new(false, HostStartMutexName);

    private readonly Lock _endpointLock = new();
    private string? _cachedEndpoint;

    public async Task<bool> IsHostReachableAsync(CancellationToken ct = default)
    {
        try
        {
            var token = await tokenProvider.GetTokenAsync(ct);
            var port = GetDiscoveredEndpoint();
            var channel = CreateAuthenticatedChannel(token ?? string.Empty, port);
            using (channel)
            {
                var diagnostics = new DiagnosticsService.DiagnosticsServiceClient(channel);
                var response = await diagnostics.GetHealthAsync(new HealthCheckRequest(),
                    deadline: DateTime.UtcNow.AddMilliseconds(500), cancellationToken: ct);
                return response.Status == HealthStatus.Healthy;
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
        {
            logger.LogWarning("Host is reachable but rejected authentication. Assuming Host is running.");
            return true;
        }
        catch
        {
            // Clear cached port on connection failure to re-read on next attempt
            lock (_endpointLock)
            {
                _cachedEndpoint = null;
            }

            return false;
        }
    }

    public async Task StartHostAsync(bool forceRestart = false, CancellationToken ct = default)
    {
        // CRITICAL: Use global mutex to prevent multiple Client instances from starting Host simultaneously
        // This handles the case where user launches Client multiple times quickly
        logger.LogInformation("Attempting to acquire Host start mutex...");

        var mutexAcquired = false;
        try
        {
            // Try to acquire mutex with timeout
            try
            {
                mutexAcquired = HostStartMutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                logger.LogWarning("Host start mutex was abandoned by a crashed instance. Taking ownership.");
                mutexAcquired = true;
            }

            if (!mutexAcquired)
            {
                logger.LogWarning(
                    "⚠️ Could not acquire Host start mutex within 30 seconds. Another Client may be starting Host.");
                logger.LogInformation("Will check if Host is already running...");

                // Even without mutex, check if Host is reachable
                if (await IsHostReachableAsync(ct))
                {
                    logger.LogInformation(
                        "✅ Host is reachable (started by another Client instance). No action needed.");
                    return;
                }

                logger.LogWarning("Host not reachable and mutex timeout. Will attempt start anyway.");
            }
            else
            {
                logger.LogInformation("✅ Acquired Host start mutex. Proceeding with Host startup check.");
            }

            var existingProcesses = Process.GetProcessesByName("Axorith.Host");

            if (existingProcesses.Length > 0)
            {
                logger.LogInformation("Found {Count} existing Axorith.Host process(es)", existingProcesses.Length);

                if (!forceRestart)
                {
                    // First quick check
                    var reachable = await IsHostReachableAsync(ct);
                    if (reachable)
                    {
                        logger.LogInformation(
                            "✅ Axorith.Host process is already running and reachable. Skipping start command.");
                        return;
                    }

                    // Extended grace period: Host initialization can take 5-10 seconds
                    // This includes: auth token generation, module registry init, port binding, file writes
                    logger.LogInformation(
                        "Host process detected but not yet reachable. Waiting up to 10 seconds for initialization...");
                    var graceSw = Stopwatch.StartNew();
                    var graceLastLogMs = 0L;

                    while (graceSw.ElapsedMilliseconds < 10000) // 10 seconds grace period
                    {
                        // Log progress every 2 seconds
                        if (graceSw.ElapsedMilliseconds - graceLastLogMs > 2000)
                        {
                            logger.LogInformation("Still waiting for Host... ({ElapsedMs}ms / 10000ms)",
                                graceSw.ElapsedMilliseconds);
                            graceLastLogMs = graceSw.ElapsedMilliseconds;
                        }

                        await Task.Delay(500, ct); // Check every 500ms

                        if (await IsHostReachableAsync(ct))
                        {
                            logger.LogInformation(
                                "✅ Axorith.Host became reachable after {ElapsedMs}ms. Skipping restart.",
                                graceSw.ElapsedMilliseconds);
                            return;
                        }
                    }

                    logger.LogWarning(
                        "⚠️ Axorith.Host process detected but not reachable after {TimeoutMs}ms grace period. Will restart.",
                        graceSw.ElapsedMilliseconds);
                }
                else
                {
                    logger.LogInformation("Force restart requested. Stopping existing Host process(es)...");
                }

                // Kill all existing Host processes
                foreach (var proc in existingProcesses)
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            logger.LogInformation("Killing Host process PID {Pid}", proc.Id);
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(2000);
                        }

                        proc.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to kill Host process PID {Pid}", proc.Id);
                    }
                }

                // Wait for processes to fully terminate and release resources
                logger.LogInformation("Waiting for Host processes to fully terminate...");
                await Task.Delay(1500, ct);

                // Verify all processes are gone
                var remainingProcesses = Process.GetProcessesByName("Axorith.Host");
                if (remainingProcesses.Length > 0)
                {
                    logger.LogError("⚠️ {Count} Host process(es) still running after kill attempt!",
                        remainingProcesses.Length);
                    foreach (var proc in remainingProcesses)
                    {
                        logger.LogError("Zombie process: PID {Pid}, Started: {StartTime}",
                            proc.Id, proc.StartTime);
                        proc.Dispose();
                    }
                }
            }
            else
            {
                logger.LogInformation("No existing Host processes found. Will start new Host.");
            }

            var startTimestampUtc = DateTime.UtcNow;

            // Clear cached port before starting
            lock (_endpointLock)
            {
                _cachedEndpoint = null;
            }

            // Check if configured port is available
            var configuredPort = config.Value.Host.Port;
            if (!IsPortAvailable(configuredPort))
            {
                logger.LogWarning("⚠️ Configured port {Port} is already in use! Host will use dynamic port.",
                    configuredPort);
            }

            // Try to delete stale host-info.json, but don't fail if locked
            try
            {
                if (File.Exists(HostInfoPath))
                {
                    File.Delete(HostInfoPath);
                    logger.LogDebug("Deleted stale host-info.json before starting new Host");
                }
            }
            catch (IOException ex)
            {
                // File might be locked by another process - this is OK, we'll wait for fresh write
                logger.LogDebug(ex, "Could not delete host-info.json (file locked). Will wait for fresh write.");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to delete stale host-info.json; will wait for a fresh write");
            }

            var exe = FindHostExecutable();
            if (exe == null)
            {
                logger.LogError("Host executable not found");
                return;
            }

            logger.LogInformation("Starting new Host process from {Executable}", exe);

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
            });

            // Wait for Host to write host-info.json with proper timestamp validation
            var sw = Stopwatch.StartNew();
            var maxWaitMs = 15000; // Increased from 8s to 15s for slower systems
            var lastLogMs = 0L;

            while (sw.ElapsedMilliseconds < maxWaitMs)
            {
                // Log progress every 3 seconds
                if (sw.ElapsedMilliseconds - lastLogMs > 3000)
                {
                    logger.LogInformation("Waiting for Host to initialize... ({ElapsedMs}ms / {MaxMs}ms)",
                        sw.ElapsedMilliseconds, maxWaitMs);
                    lastLogMs = sw.ElapsedMilliseconds;
                }

                if (File.Exists(HostInfoPath))
                {
                    try
                    {
                        // Verify file was written AFTER we started the process
                        var writeTime = File.GetLastWriteTimeUtc(HostInfoPath);
                        if (writeTime < startTimestampUtc)
                        {
                            logger.LogDebug(
                                "host-info.json exists but is stale (written before process start). Waiting for fresh write...");
                            await Task.Delay(200, ct);
                            continue;
                        }

                        // Try to read and validate the file content
                        var content = await File.ReadAllTextAsync(HostInfoPath, ct);
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            logger.LogDebug("host-info.json is empty. Waiting for complete write...");
                            await Task.Delay(200, ct);
                            continue;
                        }

                        // Validate JSON structure
                        using var doc = JsonDocument.Parse(content);
                        if (!doc.RootElement.TryGetProperty("ipcEndpoint", out var endpointElement))
                        {
                            logger.LogDebug("host-info.json missing 'ipcEndpoint' property. Waiting for complete write...");
                            await Task.Delay(200, ct);
                            continue;
                        }

                        var endpoint = endpointElement.GetString();
                        if (string.IsNullOrWhiteSpace(endpoint))
                        {
                            logger.LogWarning("host-info.json contains invalid or empty 'ipcEndpoint'. Waiting...");
                            await Task.Delay(200, ct);
                            continue;
                        }

                        // Clear cached port to force re-read
                        lock (_endpointLock)
                        {
                            _cachedEndpoint = null;
                        }

                        logger.LogInformation(
                            "Host info file detected with valid endpoint '{Endpoint}' after {ElapsedMs}ms. Host is ready.",
                            endpoint, sw.ElapsedMilliseconds);
                        return;
                    }
                    catch (IOException ioEx)
                    {
                        // File might still be being written
                        logger.LogDebug(ioEx, "Could not read host-info.json (file locked). Waiting...");
                        await Task.Delay(200, ct);
                        continue;
                    }
                    catch (JsonException jsonEx)
                    {
                        // Partial write - wait for complete JSON
                        logger.LogDebug(jsonEx, "host-info.json contains incomplete JSON. Waiting...");
                        await Task.Delay(200, ct);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to validate host-info.json; will retry");
                        await Task.Delay(200, ct);
                        continue;
                    }
                }

                await Task.Delay(200, ct);
            }

            logger.LogWarning(
                "Host started but host-info.json not found or invalid within {TimeoutMs}ms timeout. Host may still be initializing.",
                maxWaitMs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Host");
        }
        finally
        {
            if (mutexAcquired)
            {
                try
                {
                    HostStartMutex.ReleaseMutex();
                    logger.LogInformation("Released Host start mutex");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to release Host start mutex");
                }
            }
        }
    }

    public async Task StopHostAsync(CancellationToken ct = default)
    {
        try
        {
            var token = await tokenProvider.GetTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                logger.LogWarning("Cannot stop host gracefully: Auth token not found. Will try to kill process.");
                KillHostProcess();
                return;
            }

            var port = GetDiscoveredEndpoint();
            var channel = CreateAuthenticatedChannel(token, port);
            using (channel)
            {
                var management = new HostManagement.HostManagementClient(channel);
                await management.RequestShutdownAsync(
                    new ShutdownRequest { Reason = "Client tray stop", TimeoutSeconds = 10 },
                    deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
                logger.LogInformation("Shutdown requested to Host");
            }

            logger.LogInformation("Waiting for Host process to exit...");
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2000)
            {
                var processes = Process.GetProcessesByName("Axorith.Host");
                if (processes.Length == 0)
                {
                    logger.LogInformation("Host process exited gracefully.");
                    return;
                }

                await Task.Delay(500, ct);
            }

            logger.LogWarning("Host did not exit within timeout. Forcing kill.");
            KillHostProcess();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Graceful host shutdown failed or timed out, will try to kill processes");
            KillHostProcess();
        }
    }

    public async Task RestartHostAsync(CancellationToken ct = default)
    {
        await StopHostAsync(ct);
        await Task.Delay(1000, ct);
        await StartHostAsync(forceRestart: true, ct: ct);
    }

    private GrpcChannel CreateAuthenticatedChannel(string token, string ipcEndpoint)
    {
        var clientVersion = VersionHelper.GetClientVersion();

        var credentials = CallCredentials.FromInterceptor((_, metadata) =>
        {
            metadata.Add(AuthConstants.VersionHeaderName, clientVersion);

            if (!string.IsNullOrEmpty(token))
            {
                metadata.Add(AuthConstants.TokenHeaderName, token);
            }

            return Task.CompletedTask;
        });

        var channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials);

        var channelOptions = new GrpcChannelOptions
        {
            Credentials = channelCredentials,
            UnsafeUseInsecureChannelCallCredentials = true
        };

        // IPC endpoint: use ConnectCallback for Unix Domain Socket or Named Pipe
        if (ipcEndpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return GrpcChannel.ForAddress(ipcEndpoint, channelOptions);
        }
        
        channelOptions.HttpHandler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                if (OperatingSystem.IsWindows())
                {
                    var pipe = new System.IO.Pipes.NamedPipeClientStream(
                        ".", ipcEndpoint, System.IO.Pipes.PipeDirection.InOut,
                        System.IO.Pipes.PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(5000, ct);
                    return pipe;
                }

                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(ipcEndpoint), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };

        return GrpcChannel.ForAddress("http://localhost", channelOptions);
    }

    private string GetDiscoveredEndpoint()
    {
        lock (_endpointLock)
        {
            if (_cachedEndpoint != null)
            {
                return _cachedEndpoint;
            }

            try
            {
                if (File.Exists(HostInfoPath))
                {
                    var json = File.ReadAllText(HostInfoPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ipcEndpoint", out var endpointElement))
                    {
                        var endpoint = endpointElement.GetString();
                        if (!string.IsNullOrEmpty(endpoint))
                        {
                            _cachedEndpoint = endpoint;
                            logger.LogDebug("Discovered IPC endpoint from host-info.json: {Endpoint}", endpoint);
                            return endpoint;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read host-info.json");
            }

            var fallback = ApplicationPaths.IpcEndpoint;
            _cachedEndpoint = fallback;
            logger.LogDebug("Using default IPC endpoint: {Endpoint}", fallback);
            return fallback;
        }
    }

    private void KillHostProcess()
    {
        var procs = Array.Empty<Process>();
        try
        {
            procs = Process.GetProcessesByName("Axorith.Host");
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to enumerate Axorith.Host processes");
        }

        foreach (var p in procs)
        {
            try
            {
                var pid = p.Id;
                p.Kill(entireProcessTree: true);
                _ = p.WaitForExit(3000);
                logger.LogInformation("Killed Host process PID {Pid}", pid);
            }
            catch (Exception killEx)
            {
                logger.LogWarning(killEx, "Failed to kill Host process");
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private string? FindHostExecutable()
    {
        try
        {
            #if DEBUG
            var debugProbe =
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../Axorith.Host", "Axorith.Host.exe"));
            if (File.Exists(debugProbe))
            {
                return debugProbe;
            }
            #else
            var env = Environment.GetEnvironmentVariable("AXORITH_HOST_PATH", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(env))
            {
                var expanded = Environment.ExpandEnvironmentVariables(env);
                var candidate = Path.GetFullPath(expanded);
                logger.LogInformation("Candidate: {Candidate}", candidate);
                if (Directory.Exists(candidate))
                {
                    var combined = Path.Combine(candidate, "Axorith.Host.exe");
                    if (File.Exists(combined)) return combined;
                }
                else if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            #endif
        }
        catch
        {
            // Intentionally swallow to return null; callers will log an error
        }

        return null;
    }

    private bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}