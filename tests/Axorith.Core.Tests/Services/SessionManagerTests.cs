using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Shared.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Axorith.Core.Tests.Services;

public class SessionManagerTests
{
    private readonly Mock<IModuleRegistry> _mockRegistry;
    private readonly SessionManager _sessionManager;

    public SessionManagerTests()
    {
        _mockRegistry = new Mock<IModuleRegistry>();
        _sessionManager = new SessionManager(_mockRegistry.Object, NullLogger<SessionManager>.Instance);
    }

    [Fact]
    public void ActiveSession_Initially_ShouldBeNull()
    {
        // Assert
        _sessionManager.ActiveSession.Should().BeNull();
    }

    [Fact]
    public void IsSessionRunning_Initially_ShouldBeFalse()
    {
        // Assert
        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartSessionAsync_WithValidPreset_ShouldSetActiveSession()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = CreateMockModule();
        var definition = CreateModuleDefinition(moduleId, mockModule.Object.GetType());

        var (instance, scope) = CreateInstanceTuple(mockModule.Object, definition);
        _mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((instance, scope));

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test Session",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        // Act
        await _sessionManager.StartSessionAsync(preset);

        // Assert
        _sessionManager.ActiveSession.Should().NotBeNull();
        _sessionManager.ActiveSession!.Name.Should().Be("Test Session");
        _sessionManager.IsSessionRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StartSessionAsync_ShouldCallOnSessionStartAsync()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = CreateMockModule();
        var startedTcs = new TaskCompletionSource();
        mockModule.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken _) =>
            {
                startedTcs.TrySetResult();
                await Task.CompletedTask;
            });

        var definition = CreateModuleDefinition(moduleId, mockModule.Object.GetType());
        var (instance, scope) = CreateInstanceTuple(mockModule.Object, definition);
        _mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((instance, scope));

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        // Act
        await _sessionManager.StartSessionAsync(preset);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        mockModule.Verify(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopCurrentSessionAsync_ShouldCallOnSessionEndAsync()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = CreateMockModule();
        var definition = CreateModuleDefinition(moduleId, mockModule.Object.GetType());
        var (instance, scope) = CreateInstanceTuple(mockModule.Object, definition);
        _mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((instance, scope));

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        await _sessionManager.StartSessionAsync(preset);

        // Act
        await _sessionManager.StopCurrentSessionAsync();

        // Assert
        mockModule.Verify(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()), Times.Once);
        _sessionManager.IsSessionRunning.Should().BeFalse();
        _sessionManager.ActiveSession.Should().BeNull();
    }

    [Fact]
    public async Task StartSessionAsync_WhenSessionAlreadyRunning_ShouldThrow()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = CreateMockModule();
        var definition = CreateModuleDefinition(moduleId, mockModule.Object.GetType());
        var (instance, scope) = CreateInstanceTuple(mockModule.Object, definition);
        _mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((instance, scope));

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "First",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        await _sessionManager.StartSessionAsync(preset);

        // Act
        var act = async () => await _sessionManager.StartSessionAsync(preset);

        // Assert
        await act.Should().ThrowAsync<SessionException>();
    }

    [Fact]
    public async Task StartSessionAsync_WithMultipleModules_ShouldStartAll()
    {
        // Arrange
        var mockModule1 = CreateMockModule();
        var mockModule2 = CreateMockModule();
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
        var def1 = CreateModuleDefinition(id1, mockModule1.Object.GetType());
        var def2 = CreateModuleDefinition(id2, mockModule2.Object.GetType());
        var inst1 = CreateInstanceTuple(mockModule1.Object, def1);
        var inst2 = CreateInstanceTuple(mockModule2.Object, def2);
        _mockRegistry.Setup(r => r.CreateInstance(id1)).Returns(inst1);
        _mockRegistry.Setup(r => r.CreateInstance(id2)).Returns(inst2);

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

        // Act
        await _sessionManager.StartSessionAsync(preset);
        await Task.WhenAll(started1.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            started2.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        // Assert
        mockModule1.Verify(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockModule2.Verify(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WithRunningSession_ShouldStopIt()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = CreateMockModule();
        var definition = CreateModuleDefinition(moduleId, mockModule.Object.GetType());
        var (instance, scope) = CreateInstanceTuple(mockModule.Object, definition);
        _mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((instance, scope));

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        await _sessionManager.StartSessionAsync(preset);

        // Act
        await _sessionManager.DisposeAsync();

        // Assert
        mockModule.Verify(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()), Times.Once);
        _sessionManager.IsSessionRunning.Should().BeFalse();
        _sessionManager.ActiveSession.Should().BeNull();
    }

    private Mock<IModule> CreateMockModule()
    {
        var mock = new Mock<IModule>();

        mock.Setup(m => m.GetSettings()).Returns([]);
        mock.Setup(m => m.GetActions()).Returns([]);
        mock.Setup(m => m.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success);

        return mock;
    }

    private ModuleDefinition CreateModuleDefinition(Guid id, Type moduleType)
    {
        return new ModuleDefinition
        {
            Id = id,
            Name = "Test",
            Platforms = [Platform.Windows],
            ModuleType = moduleType
        };
    }

    private (IModule Instance, ILifetimeScope Scope) CreateInstanceTuple(IModule module, ModuleDefinition definition)
    {
        var root = new ContainerBuilder().Build();
        var scope = root.BeginLifetimeScope(b => b.RegisterInstance(definition).As<ModuleDefinition>());
        return (module, scope);
    }
}