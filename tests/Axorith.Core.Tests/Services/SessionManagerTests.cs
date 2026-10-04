using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Shared.Exceptions;
using FluentAssertions;
using Moq;

namespace Axorith.Core.Tests.Services;

public class SessionManagerTests
{
    private readonly Mock<IModuleRegistry> _mockRegistry;
    private readonly SessionManager _sessionManager;

    public SessionManagerTests()
    {
        _mockRegistry = new Mock<IModuleRegistry>();
        _sessionManager = SessionManagerTestFactory.CreateManager(_mockRegistry.Object);
    }

    [Fact]
    public void ActiveSession_Initially_ShouldBeNull() => _sessionManager.ActiveSession.Should().BeNull();

    [Fact]
    public void IsSessionRunning_Initially_ShouldBeFalse() => _sessionManager.IsSessionRunning.Should().BeFalse();

    [Fact]
    public async Task StartSessionAsync_WithValidPreset_ShouldSetActiveSession()
    {
        var moduleId = Guid.NewGuid();
        var mockModule = SessionManagerTestFactory.CreateModule();
        SessionManagerTestFactory.Register(_mockRegistry, mockModule, moduleId);

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test Session",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        await _sessionManager.StartSessionAsync(preset);

        _sessionManager.ActiveSession.Should().NotBeNull();
        _sessionManager.ActiveSession!.Name.Should().Be("Test Session");
        _sessionManager.IsSessionRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StartSessionAsync_ShouldCallOnSessionStartAsync()
    {
        var moduleId = Guid.NewGuid();
        var mockModule = SessionManagerTestFactory.CreateModule();
        var startedTcs = new TaskCompletionSource();
        mockModule.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken _) =>
            {
                startedTcs.TrySetResult();
                await Task.CompletedTask;
            });
        SessionManagerTestFactory.Register(_mockRegistry, mockModule, moduleId);

        var preset = SessionManagerTestFactory.CreatePreset(moduleId);

        await _sessionManager.StartSessionAsync(preset);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        mockModule.Verify(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopCurrentSessionAsync_ShouldCallOnSessionEndAsync()
    {
        var moduleId = Guid.NewGuid();
        var mockModule = SessionManagerTestFactory.CreateModule();
        SessionManagerTestFactory.Register(_mockRegistry, mockModule, moduleId);

        var preset = SessionManagerTestFactory.CreatePreset(moduleId);

        await _sessionManager.StartSessionAsync(preset);

        await _sessionManager.StopCurrentSessionAsync();

        mockModule.Verify(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()), Times.Once);
        _sessionManager.IsSessionRunning.Should().BeFalse();
        _sessionManager.ActiveSession.Should().BeNull();
    }

    [Fact]
    public async Task StartSessionAsync_WhenSessionAlreadyRunning_ShouldThrow()
    {
        var moduleId = Guid.NewGuid();
        var mockModule = SessionManagerTestFactory.CreateModule();
        SessionManagerTestFactory.Register(_mockRegistry, mockModule, moduleId);

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "First",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        await _sessionManager.StartSessionAsync(preset);

        var act = async () => await _sessionManager.StartSessionAsync(preset);

        await act.Should().ThrowAsync<SessionException>();
    }

    [Fact]
    public async Task StartSessionAsync_WithMultipleModules_ShouldStartAll()
    {
        var mockModule1 = SessionManagerTestFactory.CreateModule();
        var mockModule2 = SessionManagerTestFactory.CreateModule();
        var started1 = new TaskCompletionSource();
        var started2 = new TaskCompletionSource();
        mockModule1.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => started1.TrySetResult())
            .Returns(Task.CompletedTask);
        mockModule2.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => started2.TrySetResult())
            .Returns(Task.CompletedTask);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        SessionManagerTestFactory.Register(_mockRegistry, mockModule1, id1);
        SessionManagerTestFactory.Register(_mockRegistry, mockModule2, id2);

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Multi-Module",
            Modules =
            [
                new ConfiguredModule { ModuleId = id1 },
                new ConfiguredModule { ModuleId = id2 }
            ]
        };

        await _sessionManager.StartSessionAsync(preset);
        await Task.WhenAll(started1.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            started2.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        mockModule1.Verify(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockModule2.Verify(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WithRunningSession_ShouldStopIt()
    {
        var moduleId = Guid.NewGuid();
        var mockModule = SessionManagerTestFactory.CreateModule();
        SessionManagerTestFactory.Register(_mockRegistry, mockModule, moduleId);

        var preset = SessionManagerTestFactory.CreatePreset(moduleId);

        await _sessionManager.StartSessionAsync(preset);

        await _sessionManager.DisposeAsync();

        mockModule.Verify(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()), Times.Once);
        _sessionManager.IsSessionRunning.Should().BeFalse();
        _sessionManager.ActiveSession.Should().BeNull();
    }
}
