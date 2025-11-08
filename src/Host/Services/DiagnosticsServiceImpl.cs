using System.Reflection;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Axorith.Host.Services;

/// <summary>
///     gRPC service implementation for diagnostics and health checks.
/// </summary>
public class DiagnosticsServiceImpl : DiagnosticsService.DiagnosticsServiceBase
{
    private readonly ISessionManager _sessionManager;
    private readonly IModuleRegistry _moduleRegistry;
    private readonly ILogger<DiagnosticsServiceImpl> _logger;
    private readonly DateTimeOffset _startTime;

    public DiagnosticsServiceImpl(ISessionManager sessionManager, IModuleRegistry moduleRegistry,
        ILogger<DiagnosticsServiceImpl> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _startTime = DateTimeOffset.UtcNow;
    }

    public override Task<HealthCheckResponse> GetHealth(HealthCheckRequest request, ServerCallContext context)
    {
        try
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "0.0.1-alpha";

            var activeSessions = _sessionManager.ActiveSession != null ? 1 : 0;
            int loadedModules;
            try
            {
                loadedModules = _moduleRegistry.GetAllDefinitions().Count;
            }
            catch (InvalidOperationException ex)
            {
                // ModuleRegistry not initialized yet – treat as 0 modules but overall Healthy so the host is considered ready
                _logger.LogWarning(ex, "ModuleRegistry not initialized during health check");
                loadedModules = 0;
            }

            // Determine health status
            var status = HealthStatus.Healthy;

            // Could add more sophisticated health checks here
            // e.g., check if modules are loading correctly, sessions are responsive, etc.

            var response = new HealthCheckResponse
            {
                Status = status,
                Version = version,
                UptimeStarted = Timestamp.FromDateTimeOffset(_startTime),
                ActiveSessions = activeSessions,
                LoadedModules = loadedModules
            };

            _logger.LogDebug("Health check: {Status}, Modules: {Count}, Sessions: {Sessions}",
                status, loadedModules, activeSessions);

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
            throw new RpcException(new Status(StatusCode.Internal, "Health check failed", ex));
        }
    }
}