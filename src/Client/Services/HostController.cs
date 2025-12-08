using System.Diagnostics;
using System.Text.Json;
using Axorith.Client.Services.Abstractions;
using Axorith.Contracts;
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
    private static readonly string HostInfoPath = Path.Combine(
        Environment.ExpandEnvironmentVariables("%AppData%/Axorith"), "host-info.json");

    private readonly object _portLock = new();
    private int? _cachedPort;

    public async Task<bool> IsHostReachableAsync(CancellationToken ct = default)
    {
        try
        {
            var token = await tokenProvider.GetTokenAsync(ct);
            var port = GetDiscoveredPort();
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
            lock (_portLock)
            {
                _cachedPort = null;
            }

            return false;
        }
    }

    public async Task StartHostAsync(bool forceRestart = false, CancellationToken ct = default)
    {
        var existingProcesses = Process.GetProcessesByName("Axorith.Host");
        if (existingProcesses.Length > 0)
        {
            if (!forceRestart)
            {
                var reachable = await IsHostReachableAsync(ct);
                if (reachable)
                {
                    logger.LogInformation("Axorith.Host process is already running. Skipping start command.");
                    return;
                }

                var graceSw = Stopwatch.StartNew();
                while (graceSw.ElapsedMilliseconds < 2000)
                {
                    await Task.Delay(200, ct);
                    if (await IsHostReachableAsync(ct))
                    {
                        logger.LogInformation("Axorith.Host became reachable during grace period. Skipping restart.");
                        return;
                    }
                }
            }

            logger.LogWarning("Axorith.Host process detected but not reachable. Restarting...");
            KillHostProcess();
        }

        var startTimestampUtc = DateTime.UtcNow;
        try
        {
            if (File.Exists(HostInfoPath))
            {
                File.Delete(HostInfoPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to delete stale host-info.json; will wait for a fresh write");
        }
        finally
        {
            lock (_portLock)
            {
                _cachedPort = null;
            }
        }

        var exe = FindHostExecutable();
        if (exe == null)
        {
            logger.LogError("Host executable not found");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
            });

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 8000)
            {
                if (File.Exists(HostInfoPath))
                {
                    try
                    {
                        var writeTime = File.GetLastWriteTimeUtc(HostInfoPath);
                        if (writeTime < startTimestampUtc)
                        {
                            await Task.Delay(100, ct);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to inspect host-info.json timestamp; assuming ready");
                    }

                    // Clear cached port to force re-read
                    lock (_portLock)
                    {
                        _cachedPort = null;
                    }

                    logger.LogInformation("Host info file detected, host is ready");
                    return;
                }

                await Task.Delay(100, ct);
            }

            logger.LogWarning("Host started but host-info.json not found within timeout");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Host");
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

            var port = GetDiscoveredPort();
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
            while (sw.ElapsedMilliseconds < 2000) // Wait up to 2 seconds
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
            // Return cached port if available
            if (_cachedPort.HasValue)
            {
                return _cachedPort.Value;
            }

            // Try to read from host-info.json
            try
            {
                if (File.Exists(HostInfoPath))
                {
                    var json = File.ReadAllText(HostInfoPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("port", out var portElement))
                    {
                        var port = portElement.GetInt32();
                        _cachedPort = port;
                        logger.LogDebug("Discovered host port {Port} from host-info.json", port);
                        return port;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read host-info.json, using configured port");
            }

            // Fall back to configured port
            var fallbackPort = config.Value.Host.Port;
            _cachedPort = fallbackPort;
            logger.LogDebug("Using configured port {Port}", fallbackPort);
            return fallbackPort;
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
}