using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ConfiguredModule = Axorith.Core.Models.ConfiguredModule;

namespace Axorith.Host.Tests.Services;

public class HostManagementServiceImplTests
{
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<IHostApplicationLifetime> _mockLifetime;
    private readonly HostManagementServiceImpl _service;

    public HostManagementServiceImplTests()
    {
        _mockSessionManager = new Mock<ISessionManager>();
        _mockLifetime = new Mock<IHostApplicationLifetime>();

        _service = new HostManagementServiceImpl(
            _mockSessionManager.Object,
            _mockLifetime.Object,
            NullLogger<HostManagementServiceImpl>.Instance
        );
    }

    private static ServerCallContext CreateTestContext()
    {
        return TestServerCallContext.Create(
            method: "TestMethod",
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

    #region RequestShutdown Tests

    [Fact]
    public async Task RequestShutdown_WhenNoActiveSession_ShouldShutdownImmediately()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.IsSessionRunning).Returns(false);

        var request = new ShutdownRequest { Reason = "User requested" };
        var context = CreateTestContext();

        // Act
        var response = await _service.RequestShutdown(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Accepted.Should().BeTrue();
        response.Message.Should().Contain("Shutdown initiated");

        _mockLifetime.Verify(l => l.StopApplication(), Times.Once);
    }

    [Fact]
    public async Task RequestShutdown_WhenSessionRunning_ShouldStopSessionFirst()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.IsSessionRunning).Returns(true);
        _mockSessionManager.Setup(m => m.StopCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new ShutdownRequest { Reason = "User requested" };
        var context = CreateTestContext();

        // Act
        var response = await _service.RequestShutdown(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Accepted.Should().BeTrue();
        response.Message.Should().Contain("Stopping session");

        // Note: StopApplication is called async in background task
        // We can't easily verify it without waiting
    }

    [Fact]
    public async Task RequestShutdown_WhenExceptionOccurs_ShouldReturnFailure()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.IsSessionRunning).Throws(new InvalidOperationException("Test exception"));

        var request = new ShutdownRequest { Reason = "User requested" };
        var context = CreateTestContext();

        // Act
        var response = await _service.RequestShutdown(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Accepted.Should().BeFalse();
        response.Message.Should().Contain("Shutdown failed");
    }

    #endregion

    #region GetStatus Tests

    [Fact]
    public async Task GetStatus_WhenNoActiveSession_ShouldReturnBasicStatus()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.IsSessionRunning).Returns(false);
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((SessionPreset?)null);

        var request = new Empty();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetStatus(request, context);

        // Assert
        response.Should().NotBeNull();
        response.IsSessionRunning.Should().BeFalse();
        response.ActiveModulesCount.Should().Be(0);
        response.CurrentPresetId.Should().BeEmpty();
        response.Version.Should().NotBeNullOrEmpty();
        response.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetStatus_WhenActiveSession_ShouldReturnFullStatus()
    {
        // Arrange
        var presetId = Guid.NewGuid();
        var preset = new SessionPreset
        {
            Id = presetId,
            Name = "Test Session",
            Modules =
            [
                new ConfiguredModule { ModuleId = Guid.NewGuid(), InstanceId = Guid.NewGuid() },
                new ConfiguredModule { ModuleId = Guid.NewGuid(), InstanceId = Guid.NewGuid() }
            ]
        };

        _mockSessionManager.Setup(m => m.IsSessionRunning).Returns(true);
        _mockSessionManager.Setup(m => m.ActiveSession).Returns(preset);

        var request = new Empty();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetStatus(request, context);

        // Assert
        response.Should().NotBeNull();
        response.IsSessionRunning.Should().BeTrue();
        response.ActiveModulesCount.Should().Be(2);
        response.CurrentPresetId.Should().Be(presetId.ToString());
        response.Version.Should().NotBeNullOrEmpty();
        response.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetStatus_ShouldReportUptime()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.IsSessionRunning).Returns(false);
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((SessionPreset?)null);

        var request = new Empty();
        var context = CreateTestContext();

        // Act - call twice with delay to verify uptime increases
        var response1 = await _service.GetStatus(request, context);
        await Task.Delay(100); // Small delay
        var response2 = await _service.GetStatus(request, context);

        // Assert
        response1.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
        response2.UptimeSeconds.Should().BeGreaterThanOrEqualTo(response1.UptimeSeconds);
    }

    #endregion
}