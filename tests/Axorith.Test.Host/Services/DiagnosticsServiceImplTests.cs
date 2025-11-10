using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using FluentAssertions;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;

namespace Axorith.Test.Host.Services;

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

    private static Grpc.Core.ServerCallContext CreateTestContext()
    {
        return TestServerCallContext.Create(
            method: "test",
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(5),
            requestHeaders: new Grpc.Core.Metadata(),
            cancellationToken: CancellationToken.None,
            peer: "127.0.0.1",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: (metadata) => Task.CompletedTask,
            writeOptionsGetter: () => new Grpc.Core.WriteOptions(),
            writeOptionsSetter: (writeOptions) => { }
        );
    }

    [Fact]
    public async Task GetHealth_ShouldReturnHealthy()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((Core.Models.SessionPreset?)null);
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
        var preset = new Core.Models.SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Active Session",
            Modules = new List<Core.Models.ConfiguredModule>()
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
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((Core.Models.SessionPreset?)null);
        
        var modules = new List<ModuleDefinition>
        {
            new ModuleDefinition { Id = Guid.NewGuid(), Name = "Module1" },
            new ModuleDefinition { Id = Guid.NewGuid(), Name = "Module2" },
            new ModuleDefinition { Id = Guid.NewGuid(), Name = "Module3" }
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
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((Core.Models.SessionPreset?)null);
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
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((Core.Models.SessionPreset?)null);
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
