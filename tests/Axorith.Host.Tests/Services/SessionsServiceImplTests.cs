using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using Axorith.Host.Streaming;
using Axorith.Shared.Exceptions;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ConfiguredModule = Axorith.Core.Models.ConfiguredModule;

namespace Axorith.Host.Tests.Services;

public class SessionsServiceImplTests
{
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<IPresetManager> _mockPresetManager;
    private readonly Mock<IModuleRegistry> _mockModuleRegistry;
    private readonly SessionsServiceImpl _service;

    public SessionsServiceImplTests()
    {
        _mockSessionManager = new Mock<ISessionManager>();
        _mockPresetManager = new Mock<IPresetManager>();
        _mockModuleRegistry = new Mock<IModuleRegistry>();

        // Create real broadcaster with mocked dependencies
        var broadcaster = new SessionEventBroadcaster(
            _mockSessionManager.Object,
            NullLogger<SessionEventBroadcaster>.Instance
        );

        _service = new SessionsServiceImpl(
            _mockSessionManager.Object,
            _mockPresetManager.Object,
            broadcaster,
            NullLogger<SessionsServiceImpl>.Instance
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

    #region GetSessionState Tests

    [Fact]
    public async Task GetSessionState_WhenNoActiveSession_ShouldReturnInactive()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.ActiveSession).Returns((SessionPreset?)null);
        var request = new GetSessionStateRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetSessionState(request, context);

        // Assert
        response.Should().NotBeNull();
        response.IsActive.Should().BeFalse();
        response.PresetId.Should().BeEmpty();
        response.ModuleStates.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSessionState_WhenActiveSession_ShouldReturnActive()
    {
        // Arrange
        var presetId = Guid.NewGuid();
        var preset = new SessionPreset
        {
            Id = presetId,
            Name = "Test Session",
            Modules = []
        };

        var snapshot = new SessionSnapshot(
            presetId,
            preset.Name,
            []);

        _mockSessionManager.Setup(m => m.GetCurrentSnapshot()).Returns(snapshot);

        var request = new GetSessionStateRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetSessionState(request, context);

        // Assert
        response.Should().NotBeNull();
        response.IsActive.Should().BeTrue();
        response.PresetId.Should().Be(presetId.ToString());
        response.PresetName.Should().Be("Test Session");
    }

    [Fact]
    public async Task GetSessionState_WithModules_ShouldReturnModuleStates()
    {
        // Arrange
        var presetId = Guid.NewGuid();

        var moduleId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        var configuredModule = new ConfiguredModule
        {
            ModuleId = moduleId,
            InstanceId = instanceId,
            CustomName = "Test Module Instance"
        };

        var preset = new SessionPreset
        {
            Id = presetId,
            Name = "Test Session",
            Modules = [configuredModule]
        };

        var moduleSnapshot = new SessionModuleSnapshot(
            instanceId,
            moduleId,
            "Test Module",
            configuredModule.CustomName,
            [],
            []);

        var snapshot = new SessionSnapshot(
            presetId,
            preset.Name,
            [moduleSnapshot]);

        _mockSessionManager.Setup(m => m.GetCurrentSnapshot()).Returns(snapshot);

        var request = new GetSessionStateRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.GetSessionState(request, context);

        // Assert
        response.Should().NotBeNull();
        response.IsActive.Should().BeTrue();
        response.ModuleStates.Should().HaveCount(1);

        var moduleState = response.ModuleStates[0];
        moduleState.InstanceId.Should().Be(instanceId.ToString());
        moduleState.ModuleName.Should().Be("Test Module");
        moduleState.CustomName.Should().Be("Test Module Instance");
        moduleState.Status.Should().Be(ModuleStatus.Running);
    }

    #endregion

    #region StartSession Tests

    [Fact]
    public async Task StartSession_WithInvalidGuid_ShouldReturnFailure()
    {
        // Arrange
        var request = new StartSessionRequest { PresetId = "invalid-guid" };
        var context = CreateTestContext();

        // Act
        var response = await _service.StartSession(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Invalid preset ID");
    }

    [Fact]
    public async Task StartSession_WhenPresetNotFound_ShouldReturnFailure()
    {
        // Arrange
        var presetId = Guid.NewGuid();
        _mockPresetManager.Setup(m => m.GetPresetByIdAsync(presetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionPreset?)null);

        var request = new StartSessionRequest { PresetId = presetId.ToString() };
        var context = CreateTestContext();

        // Act
        var response = await _service.StartSession(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Preset not found");
    }

    [Fact]
    public async Task StartSession_WhenPresetExists_ShouldStartSession()
    {
        // Arrange
        var presetId = Guid.NewGuid();
        var preset = new SessionPreset
        {
            Id = presetId,
            Name = "Test Preset",
            Modules = []
        };

        _mockPresetManager.Setup(m => m.GetPresetByIdAsync(presetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preset);

        _mockSessionManager.Setup(m => m.StartSessionAsync(preset, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new StartSessionRequest { PresetId = presetId.ToString() };
        var context = CreateTestContext();

        // Act
        var response = await _service.StartSession(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Message.Should().Contain("started successfully");

        _mockSessionManager.Verify(m => m.StartSessionAsync(preset, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartSession_WhenSessionException_ShouldReturnFailure()
    {
        // Arrange
        var presetId = Guid.NewGuid();
        var preset = new SessionPreset
        {
            Id = presetId,
            Name = "Test Preset",
            Modules = []
        };

        _mockPresetManager.Setup(m => m.GetPresetByIdAsync(presetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preset);

        _mockSessionManager.Setup(m => m.StartSessionAsync(preset, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SessionException("Session already active"));

        var request = new StartSessionRequest { PresetId = presetId.ToString() };
        var context = CreateTestContext();

        // Act
        var response = await _service.StartSession(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Session already active");
    }

    [Fact]
    public async Task StartSession_WhenInvalidSettingsException_ShouldReturnFailureWithErrors()
    {
        // Arrange
        var presetId = Guid.NewGuid();
        var preset = new SessionPreset
        {
            Id = presetId,
            Name = "Test Preset",
            Modules = []
        };

        _mockPresetManager.Setup(m => m.GetPresetByIdAsync(presetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preset);

        var invalidKeys = new List<string> { "Setting1", "Setting2" };
        _mockSessionManager.Setup(m => m.StartSessionAsync(preset, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidSettingsException("Invalid settings", invalidKeys));

        var request = new StartSessionRequest { PresetId = presetId.ToString() };
        var context = CreateTestContext();

        // Act
        var response = await _service.StartSession(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Errors.Should().HaveCount(2);
        response.Errors.Should().Contain(e => e.Contains("Setting1"));
        response.Errors.Should().Contain(e => e.Contains("Setting2"));
    }

    #endregion

    #region StopSession Tests

    [Fact]
    public async Task StopSession_WhenSuccess_ShouldReturnSuccess()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.StopCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new StopSessionRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.StopSession(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Message.Should().Contain("stopped successfully");

        _mockSessionManager.Verify(m => m.StopCurrentSessionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopSession_WhenSessionException_ShouldReturnFailure()
    {
        // Arrange
        _mockSessionManager.Setup(m => m.StopCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SessionException("No active session"));

        var request = new StopSessionRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.StopSession(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("No active session");
    }

    #endregion
}