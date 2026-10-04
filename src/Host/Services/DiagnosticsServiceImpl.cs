using System.Reflection;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Axorith.Host.Services;

public class DiagnosticsServiceImpl(
    ISessionManager sessionManager,
    IModuleRegistry moduleRegistry,
    ILogger<DiagnosticsServiceImpl> logger)
    : DiagnosticsService.DiagnosticsServiceBase
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public override Task<HealthCheckResponse> GetHealth(HealthCheckRequest request, ServerCallContext context)
    {

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.1-alpha";

        var activeSessions = sessionManager.ActiveSession != null ? 1 : 0;
        int loadedModules;
        try
        {
            loadedModules = moduleRegistry.GetAllDefinitions().Count;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ModuleRegistry not initialized during health check");
            loadedModules = 0;
        }

        const HealthStatus status = HealthStatus.Healthy;

        var response = new HealthCheckResponse
        {
            Status = status,
            Version = version,
            UptimeStarted = Timestamp.FromDateTimeOffset(_startTime),
            ActiveSessions = activeSessions,
            LoadedModules = loadedModules
        };

        logger.LogDebug("Health check: {Status}, Modules: {Count}, Sessions: {Sessions}",
            status, loadedModules, activeSessions);

        return Task.FromResult(response);

    }
}
