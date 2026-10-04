using System.Diagnostics;
using Axorith.Client.Services.Abstractions;
using Axorith.Contracts;
using Axorith.Shared.Utils;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axorith.Client.Services;

public sealed class HostController(
    IOptions<Configuration> config,
    ILogger<HostController> logger,
    ITokenProvider tokenProvider)
{
    private static readonly string HostInfoPath = ApplicationPaths.HostInfoFile;
    private static readonly Mutex HostStartMutex = new(false, "Global\\AxorithHostStartMutex");

    private readonly object _portLock = new();
    private int? _cachedPort;

    public async Task<bool> IsHostReachableAsync(CancellationToken ct = default)
    {
        try
        {
            var token = await tokenProvider.GetTokenAsync(ct);
            var port = GetDiscoveredPort();
            using var channel = CreateAuthenticatedChannel(token ?? string.Empty, port);
            var diagnostics = new DiagnosticsService.DiagnosticsServiceClient(channel);
            var response = await diagnostics.GetHealthAsync(new HealthCheckRequest(),
                deadline: DateTime.UtcNow.AddMilliseconds(500), cancellationToken: ct);
            return response.Status == HealthStatus.Healthy;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
        {
            logger.LogWarning("Host is reachable but rejected authentication. Assuming Host is running.");
            return true;
        }
        catch
        {
            lock (_portLock)
            {
                _cachedPort = null;
            }

            return false;
        }
    }

    public async Task StartHostAsync(bool forceRestart = false, CancellationToken ct = default)
    {
        var mutexAcquired = false;
        try
        {
            mutexAcquired = HostStartMutex.WaitOne(TimeSpan.FromSeconds(30));

            if (!mutexAcquired)
            {
                logger.LogWarning(
                    "Could not acquire Host start mutex within 30 seconds; another Client may be starting Host.");

                if (await IsHostReachableAsync(ct))
                {
                    return;
                }

                logger.LogWarning("Host not reachable and mutex timeout. Will attempt start anyway.");
            }

            var existingProcesses = Process.GetProcessesByName("Axorith.Host");

            if (existingProcesses.Length > 0)
            {
                logger.LogInformation("Found {Count} existing Axorith.Host process(es)", existingProcesses.Length);

                if (!forceRestart)
                {
                    var reachable = await IsHostReachableAsync(ct);
                    if (reachable)
                    {
                        return;
                    }

                    logger.LogInformation(
                        "Host process detected but not yet reachable. Waiting up to 10 seconds for initialization...");
                    var graceSw = Stopwatch.StartNew();
                    var graceLastLogMs = 0L;

                    while (graceSw.ElapsedMilliseconds < 10000)
                    {
                        if (graceSw.ElapsedMilliseconds - graceLastLogMs > 2000)
                        {
                            logger.LogInformation("Still waiting for Host... ({ElapsedMs}ms / 10000ms)",
                                graceSw.ElapsedMilliseconds);
                            graceLastLogMs = graceSw.ElapsedMilliseconds;
                        }


                        if (await IsHostReachableAsync(ct))
                        {
                            return;
                        }

                        await Task.Delay(200, ct);
                    }

                    logger.LogWarning(
                        "Axorith.Host process detected but not reachable after {TimeoutMs}ms grace period.",
                        graceSw.ElapsedMilliseconds);
                    foreach (var process in existingProcesses) process.Dispose();
                    throw new InvalidOperationException(
                        "Axorith Host is running but unavailable. It was left running to protect any committed session; retry after it recovers or request an explicit restart.");
                }

                logger.LogInformation("Force restart requested. Stopping existing Host process(es)...");

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

                logger.LogInformation("Waiting for Host processes to fully terminate...");
                await Task.Delay(1500, ct);

                var remainingProcesses = Process.GetProcessesByName("Axorith.Host");
                if (remainingProcesses.Length > 0)
                {
                    logger.LogError("{Count} Host process(es) still running after kill attempt",
                        remainingProcesses.Length);
                    foreach (var proc in remainingProcesses)
                    {
                        logger.LogError("Zombie process: PID {Pid}, Started: {StartTime}",
                            proc.Id, proc.StartTime);
                        proc.Dispose();
                    }

                    throw new InvalidOperationException(
                        "The existing Axorith Host could not be stopped; refusing to start a second Host process.");
                }
            }
            else
            {
                logger.LogInformation("No existing Host processes found. Will start new Host.");
            }

            var startTimestampUtc = DateTime.UtcNow;

            lock (_portLock)
            {
                _cachedPort = null;
            }

            try
            {
                if (File.Exists(HostInfoPath))
                {
                    File.Delete(HostInfoPath);
                    logger.LogDebug("Deleted stale host-info.json before starting new Host");
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not delete stale host-info.json; will wait for a fresh write.");
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
                Verb = OperatingSystem.IsWindows() ? "runas" : string.Empty,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
            });

            var sw = Stopwatch.StartNew();
            const int maxWaitMs = 15000;
            var lastLogMs = 0L;

            while (sw.ElapsedMilliseconds < maxWaitMs)
            {
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
                        var writeTime = File.GetLastWriteTimeUtc(HostInfoPath);
                        if (writeTime < startTimestampUtc)
                        {
                            logger.LogDebug(
                                "host-info.json exists but is stale (written before process start). Waiting for fresh write...");
                            await Task.Delay(200, ct);
                            continue;
                        }

                        var content = await File.ReadAllTextAsync(HostInfoPath, ct);
                        if (!HostInfoReader.TryParsePort(content, out var port))
                        {
                            logger.LogDebug("host-info.json is incomplete or contains an invalid port. Waiting...");
                            await Task.Delay(200, ct);
                            continue;
                        }

                        lock (_portLock)
                        {
                            _cachedPort = null;
                        }

                        logger.LogInformation(
                            "Host info file detected with valid port {Port} after {ElapsedMs}ms. Host is ready.",
                            port, sw.ElapsedMilliseconds);
                        return;
                    }
                    catch (IOException ioEx)
                    {
                        logger.LogDebug(ioEx, "Could not read host-info.json (file locked). Waiting...");
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
                throw new InvalidOperationException("Cannot stop Host gracefully because its authentication token is unavailable.");
            }

            var port = GetDiscoveredPort();
            using var channel = CreateAuthenticatedChannel(token, port);
            var management = new HostManagement.HostManagementClient(channel);
            var response = await management.RequestShutdownAsync(
                new ShutdownRequest { Reason = "Client tray stop", TimeoutSeconds = 10 },
                deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
            if (!response.Accepted)
            {
                throw new InvalidOperationException(response.Message);
            }

            logger.LogInformation("Shutdown requested to Host");

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

            throw new TimeoutException("Host did not exit after accepting the shutdown request.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Graceful Host shutdown failed; leaving the process running.");
            throw;
        }
    }

    public async Task RestartHostAsync(CancellationToken ct = default)
    {
        await StopHostAsync(ct);
        await Task.Delay(1000, ct);
        await StartHostAsync(forceRestart: true, ct: ct);
    }

    private GrpcChannel CreateAuthenticatedChannel(string token, int port)
    {
        var addr = $"http://{config.Value.Host.Address}:{port}";

        var credentials = CallCredentials.FromInterceptor((_, metadata) =>
        {
            if (!string.IsNullOrEmpty(token))
            {
                metadata.Add(AuthConstants.TokenHeaderName, token);
            }

            return Task.CompletedTask;
        });

        var channelCredentials = ChannelCredentials.Create(ChannelCredentials.Insecure, credentials);

        return GrpcChannel.ForAddress(addr, new GrpcChannelOptions
        {
            Credentials = channelCredentials,
            UnsafeUseInsecureChannelCallCredentials = true
        });
    }

    private int GetDiscoveredPort()
    {
        lock (_portLock)
        {
            if (_cachedPort.HasValue)
            {
                return _cachedPort.Value;
            }

            if (HostInfoReader.TryReadPort(HostInfoPath, out var port))
            {
                _cachedPort = port;
                logger.LogDebug("Discovered host port {Port} from host-info.json", port);
                return port;
            }

            var fallbackPort = config.Value.Host.Port;
            _cachedPort = fallbackPort;
            logger.LogDebug("Using configured port {Port}", fallbackPort);
            return fallbackPort;
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


}
