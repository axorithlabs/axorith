using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Axorith.Host.Tests.Services;

/// <summary>
///     Tests for DiagnosticsServiceImpl - gRPC service for diagnostics
/// </summary>
public class DiagnosticsServiceImplTests
{
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<IModuleRegistry> _mockModuleRegistry;
    private readonly DiagnosticsServiceImpl _service;

    public DiagnosticsServiceImplTests()
    {
        _mockSessionManager = new Mock<ISessionManager>();
        _mockModuleRegistry = new Mock<IModuleRegistry>();

        _service = new DiagnosticsServiceImpl(
            _mockSessionManager.Object,
            _mockModuleRegistry.Object,
            NullLogger<DiagnosticsServiceImpl>.Instance
        );
    }
[Fact]
    public async Task GetHealth_WhenModuleRegistryNotInitialized_ShouldStillReturnHealthy()
    {
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((SessionPreset?)null);
        _mockModuleRegistry.Setup(m => m.GetAllDefinitions())
            .Throws(new InvalidOperationException("Not initialized"));

        var request = new HealthCheckRequest();
        var context = GrpcTestContext.Create();

        var response = await _service.GetHealth(request, context);

        response.Status.Should().Be(HealthStatus.Healthy);
        response.LoadedModules.Should().Be(0);
    }

}
