using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using Axorith.Host.Streaming;
using Axorith.Shared.Exceptions;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Axorith.Host.Tests.Services;

public class SessionsServiceImplTests
{
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<IPresetManager> _mockPresetManager;
    private readonly SessionsServiceImpl _service;

    public SessionsServiceImplTests()
    {
        _mockSessionManager = new Mock<ISessionManager>();
        _mockPresetManager = new Mock<IPresetManager>();

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



    [Fact]
    public async Task StartSession_WithInvalidGuid_ShouldReturnFailure()
    {
        var request = new StartSessionRequest { PresetId = "invalid-guid" };
        var context = GrpcTestContext.Create();

        var response = await _service.StartSession(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Invalid preset ID");
    }

    [Fact]
    public async Task StartSession_WhenPresetNotFound_ShouldReturnFailure()
    {
        var presetId = Guid.NewGuid();
        _mockPresetManager.Setup(m => m.GetPresetByIdAsync(presetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionPreset?)null);

        var request = new StartSessionRequest { PresetId = presetId.ToString() };
        var context = GrpcTestContext.Create();

        var response = await _service.StartSession(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Preset not found");
    }

    [Fact]
    public async Task StartSession_WhenSessionException_ShouldReturnFailure()
    {
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
        var context = GrpcTestContext.Create();

        var response = await _service.StartSession(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Session already active");
    }

    [Fact]
    public async Task StartSession_WhenInvalidSettingsException_ShouldReturnFailureWithErrors()
    {
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
        var context = GrpcTestContext.Create();

        var response = await _service.StartSession(request, context);

        response.Success.Should().BeFalse();
        response.Errors.Should().HaveCount(2);
        response.Errors.Should().Contain(e => e.Contains("Setting1"));
        response.Errors.Should().Contain(e => e.Contains("Setting2"));
    }



    [Fact]
    public async Task StopSession_WhenSessionException_ShouldReturnFailure()
    {
        _mockSessionManager.Setup(m => m.StopCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SessionException("No active session"));

        var request = new StopSessionRequest();
        var context = GrpcTestContext.Create();

        var response = await _service.StopSession(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("No active session");
    }

}
