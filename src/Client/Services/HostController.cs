using System.Diagnostics;
using Axorith.Contracts;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axorith.Client.Services;

public interface IHostController
{
    Task<bool> IsHostReachableAsync(CancellationToken ct = default);
    Task StartHostAsync(CancellationToken ct = default);
    Task StopHostAsync(CancellationToken ct = default);
    Task RestartHostAsync(CancellationToken ct = default);
}

public class HostController(IOptions<Configuration> config, ILogger<HostController> logger) : IHostController
{
    public async Task<bool> IsHostReachableAsync(CancellationToken ct = default)
    {
        try
        {
            var addr = $"http://{config.Value.Host.Address}:{config.Value.Host.Port}";
            using var channel = GrpcChannel.ForAddress(addr);
            var diagnostics = new DiagnosticsService.DiagnosticsServiceClient(channel);
            var response = await diagnostics.GetHealthAsync(new HealthCheckRequest(),
                deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: ct);
            return response.Status == HealthStatus.Healthy;
        }
        catch
        {
            return false;
        }
    }

    public async Task StartHostAsync(CancellationToken ct = default)
    {
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
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
            });
            // Give the Host a brief moment to bind ports before the first health check/connect attempt.
            await Task.Delay(1000, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Host");
        }
    }

    public async Task StopHostAsync(CancellationToken ct = default)
    {
        var addr = $"http://{config.Value.Host.Address}:{config.Value.Host.Port}";
        try
        {
            using var channel = GrpcChannel.ForAddress(addr);
            var management = new HostManagement.HostManagementClient(channel);
            await management.RequestShutdownAsync(new ShutdownRequest { Reason = "Client tray stop", TimeoutSeconds = 10 },
                deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
            logger.LogInformation("Shutdown requested to Host");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Graceful host shutdown failed, will try to kill processes");
            Process[] procs = Array.Empty<Process>();
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
                    var pid = 0;
                    try { pid = p.Id; }
                    catch
                    {
                        // ignored
                    }

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
                    try { p.Dispose(); }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }
    }

    public async Task RestartHostAsync(CancellationToken ct = default)
    {
        await StopHostAsync(ct);
        await Task.Delay(1000, ct);
        await StartHostAsync(ct);
    }

    private string? FindHostExecutable()
    {
        try
        {
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

            #if DEBUG
            var debugProbe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../Axorith.Host", "Axorith.Host.exe"));
            if (File.Exists(debugProbe)) return debugProbe;
            #endif
        }
        catch
        {
            // Intentionally swallow to return null; callers will log an error
        }

        return null;
    }
}
