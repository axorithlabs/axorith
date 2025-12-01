using System.Diagnostics;
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
    public async Task<bool> IsHostReachableAsync(CancellationToken ct = default)
    {
        try
        {
            var token = await tokenProvider.GetTokenAsync(ct);
            var channel = CreateAuthenticatedChannel(token ?? string.Empty);
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
            return false;
        }
    }

    public async Task StartHostAsync(CancellationToken ct = default)
    {
        var existingProcesses = Process.GetProcessesByName("Axorith.Host");
        if (existingProcesses.Length > 0)
        {
            logger.LogInformation("Axorith.Host process is already running. Skipping start command.");
            return;
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

            // Give the Host a brief moment to bind ports and WRITE THE TOKEN FILE.
            await Task.Delay(2000, ct);
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

            var channel = CreateAuthenticatedChannel(token);
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
        await StartHostAsync(ct);
    }

    private GrpcChannel CreateAuthenticatedChannel(string token)
    {
        var addr = $"http://{config.Value.Host.Address}:{config.Value.Host.Port}";

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