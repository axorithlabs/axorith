using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;

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

    private static ServerCallContext CreateTestContext()
    {
        return TestServerCallContext.Create(
            method: "test",
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(5),
            requestHeaders: [],
            cancellationToken: CancellationToken.None,
            peer: "127.0.0.1",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => new WriteOptions(),
            writeOptionsSetter: _ => { }
        );
    }

    [Fact]
    public async Task GetHealth_ShouldReturnHealthy()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((SessionPreset?)null);
        _mockModuleRegistry.Setup(m => m.GetAllDefinitions())
            .Returns(new List<ModuleDefinition>());

        var request = new HealthCheckRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetHealth(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(HealthStatus.Healthy);
        response.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetHealth_WithActiveSession_ShouldShowOne()
    {
        // Arrange
        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Active Session",
            Modules = []
        };

        _mockSessionManager.Setup(m => m.ActiveSession).Returns(preset);
        _mockModuleRegistry.Setup(m => m.GetAllDefinitions())
            .Returns(new List<ModuleDefinition>());

        var request = new HealthCheckRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetHealth(request, context);

        // Assert
        response.Status.Should().Be(HealthStatus.Healthy);
        response.ActiveSessions.Should().Be(1);
    }

    [Fact]
    public async Task GetHealth_WithLoadedModules_ShouldShowCount()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((SessionPreset?)null);

        var modules = new List<ModuleDefinition>
        {
            new() { Id = Guid.NewGuid(), Name = "Module1" },
            new() { Id = Guid.NewGuid(), Name = "Module2" },
            new() { Id = Guid.NewGuid(), Name = "Module3" }
        };

        _mockModuleRegistry.Setup(m => m.GetAllDefinitions()).Returns(modules);

        var request = new HealthCheckRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetHealth(request, context);

        // Assert
        response.LoadedModules.Should().Be(3);
    }

    [Fact]
    public async Task GetHealth_WhenModuleRegistryNotInitialized_ShouldStillReturnHealthy()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((SessionPreset?)null);
        _mockModuleRegistry.Setup(m => m.GetAllDefinitions())
            .Throws(new InvalidOperationException("Not initialized"));

        var request = new HealthCheckRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetHealth(request, context);

        // Assert
        response.Status.Should().Be(HealthStatus.Healthy);
        response.LoadedModules.Should().Be(0);
    }

    [Fact]
    public async Task GetHealth_ShouldHaveUptimeStarted()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((SessionPreset?)null);
        _mockModuleRegistry.Setup(m => m.GetAllDefinitions()).Returns(new List<ModuleDefinition>());

        var request = new HealthCheckRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetHealth(request, context);

        // Assert
        response.UptimeStarted.Should().NotBeNull();
        response.UptimeStarted.ToDateTime().Should().BeBefore(DateTime.UtcNow);
    }
}