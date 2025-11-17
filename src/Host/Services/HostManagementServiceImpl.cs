using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Axorith.Host.Services;

/// <summary>
///     gRPC service implementation for host lifecycle management.
/// </summary>
public class HostManagementServiceImpl(
    ISessionManager sessionManager,
    IHostApplicationLifetime lifetime,
    ILogger<HostManagementServiceImpl> logger) : HostManagement.HostManagementBase
{
    private static readonly DateTime SStartTime = DateTime.UtcNow;

    public override Task<ShutdownResponse> RequestShutdown(ShutdownRequest request, ServerCallContext context)
    {
        logger.LogInformation("Shutdown requested: {Reason}", request.Reason);

        try
        {
            if (sessionManager.IsSessionRunning)
            {
                logger.LogInformation("Stopping active session before shutdown...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await sessionManager.StopCurrentSessionAsync();
                        logger.LogInformation("Session stopped successfully");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to stop session gracefully");
                    }
                    finally
                    {
                        lifetime.StopApplication();
                    }
                });

                return Task.FromResult(new ShutdownResponse
                {
                    Accepted = true,
                    Message = "Stopping session and shutting down..."
                });
            }

            lifetime.StopApplication();

            return Task.FromResult(new ShutdownResponse
            {
                Accepted = true,
                Message = "Shutdown initiated"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during shutdown request");
            return Task.FromResult(new ShutdownResponse
            {
                Accepted = false,
                Message = $"Shutdown failed: {ex.Message}"
            });
        }
    }

    public override Task<HostStatusResponse> GetStatus(Empty request, ServerCallContext context)
    {
        var uptime = (long)(DateTime.UtcNow - SStartTime).TotalSeconds;

        var activeModuleCount = sessionManager.ActiveSession?.Modules.Count ?? 0;

        var response = new HostStatusResponse
        {
            Version = typeof(HostManagementServiceImpl).Assembly.GetName().Version?.ToString() ?? "0.0.1",
            UptimeSeconds = uptime,
            ActiveModulesCount = activeModuleCount,
            IsSessionRunning = sessionManager.IsSessionRunning,
            CurrentPresetId = sessionManager.ActiveSession?.Id.ToString() ?? string.Empty
        };

        return Task.FromResult(response);
    }
}