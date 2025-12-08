using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Shared.Exceptions;
using Axorith.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Axorith.Core.Tests.Services;

/// <summary>
///     Negative tests for SessionManager - validation failures, timeouts, rollback scenarios
/// </summary>
public class SessionManagerNegativeTests
{
    private readonly Mock<IModuleRegistry> _mockRegistry;
    private readonly SessionManager _sessionManager;

    public SessionManagerNegativeTests()
    {
        _mockRegistry = new Mock<IModuleRegistry>();
        _sessionManager = new SessionManager(
            _mockRegistry.Object,
            NullLogger<SessionManager>.Instance,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(10),
            new NoopTelemetryService()
        );
    }

    [Fact]
    public async Task StartSessionAsync_WithValidationFailure_ShouldRollbackAndStop()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = CreateMockModule();
        mockModule.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Fail("Validation failed"));

        var definition = CreateModuleDefinition(moduleId, mockModule.Object.GetType());
        var (instance, scope) = CreateInstanceTuple(mockModule.Object, definition);
        _mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((instance, scope));

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        // Act & Assert
        await _sessionManager.Invoking(sm => sm.StartSessionAsync(preset))
            .Should()
            .ThrowAsync<SessionException>();

        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartSessionAsync_WithOnSessionStartException_ShouldRollback()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = CreateMockModule();
        mockModule.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Module start failed"));

        var definition = CreateModuleDefinition(moduleId, mockModule.Object.GetType());
        var (instance, scope) = CreateInstanceTuple(mockModule.Object, definition);
        _mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((instance, scope));

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        // Act & Assert
        await _sessionManager.Invoking(sm => sm.StartSessionAsync(preset))
            .Should()
            .ThrowAsync<SessionException>();

        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartSessionAsync_WithValidationTimeout_ShouldRollback()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = CreateMockModule();
        // Simulate timeout immediately by throwing OperationCanceledException
        mockModule.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var definition = CreateModuleDefinition(moduleId, mockModule.Object.GetType());
        var (instance, scope) = CreateInstanceTuple(mockModule.Object, definition);
        _mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((instance, scope));

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        // Act & Assert
        await _sessionManager.Invoking(sm => sm.StartSessionAsync(preset))
            .Should()
            .ThrowAsync<SessionException>();

        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartSessionAsync_WithSessionStartTimeout_ShouldRollback()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = CreateMockModule();
        // Simulate startup timeout via immediate OperationCanceledException
        mockModule.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var definition = CreateModuleDefinition(moduleId, mockModule.Object.GetType());
        var (instance, scope) = CreateInstanceTuple(mockModule.Object, definition);
        _mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((instance, scope));

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        // Act & Assert
        await _sessionManager.Invoking(sm => sm.StartSessionAsync(preset))
            .Should()
            .ThrowAsync<SessionException>();

        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartSessionAsync_WithMultipleModules_OneFailsValidation_ShouldRollback()
    {
        // Arrange
        var idGood = Guid.NewGuid();
        var idBad = Guid.NewGuid();

        var goodModule = CreateMockModule();
        var badModule = CreateMockModule();

        badModule.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Fail("Bad module validation failed"));

        var goodDef = CreateModuleDefinition(idGood, goodModule.Object.GetType());
        var badDef = CreateModuleDefinition(idBad, badModule.Object.GetType());
        var goodInst = CreateInstanceTuple(goodModule.Object, goodDef);
        var badInst = CreateInstanceTuple(badModule.Object, badDef);
        _mockRegistry.Setup(r => r.CreateInstance(idGood)).Returns(goodInst);
        _mockRegistry.Setup(r => r.CreateInstance(idBad)).Returns(badInst);

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules =
            [
                new ConfiguredModule { ModuleId = idGood },
                new ConfiguredModule { ModuleId = idBad }
            ]
        };

        // Act & Assert
        await _sessionManager.Invoking(sm => sm.StartSessionAsync(preset))
            .Should()
            .ThrowAsync<SessionException>();

        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartSessionAsync_WithMultipleModules_OneFailsStart_ShouldRollbackAndStopStartedOnes()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var module1 = CreateMockModule();
        var module2 = CreateMockModule();

        var module1Stopped = false;
        module1.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()))
            .Callback(() => module1Stopped = true)
            .Returns(Task.CompletedTask);

        module2.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Module 2 failed"));

        var def1 = CreateModuleDefinition(id1, module1.Object.GetType());
        var def2 = CreateModuleDefinition(id2, module2.Object.GetType());
        var inst1 = CreateInstanceTuple(module1.Object, def1);
        var inst2 = CreateInstanceTuple(module2.Object, def2);
        _mockRegistry.Setup(r => r.CreateInstance(id1)).Returns(inst1);
        _mockRegistry.Setup(r => r.CreateInstance(id2)).Returns(inst2);

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules =
            [
                new ConfiguredModule { ModuleId = id1 },
                new ConfiguredModule { ModuleId = id2 }
            ]
        };

        // Act & Assert
        await _sessionManager.Invoking(sm => sm.StartSessionAsync(preset))
            .Should()
            .ThrowAsync<SessionException>();

        module1Stopped.Should().BeTrue("Module 1 should have been stopped during rollback");
        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StopCurrentSessionAsync_WithModuleException_ShouldStillStopOthers()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var module1 = CreateMockModule();
        var module2 = CreateMockModule();

        var module2Stopped = false;

        module1.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Module 1 stop failed"));

        module2.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()))
            .Callback(() => module2Stopped = true)
            .Returns(Task.CompletedTask);

        var def1 = CreateModuleDefinition(id1, module1.Object.GetType());
        var def2 = CreateModuleDefinition(id2, module2.Object.GetType());
        var inst1 = CreateInstanceTuple(module1.Object, def1);
        var inst2 = CreateInstanceTuple(module2.Object, def2);
        _mockRegistry.Setup(r => r.CreateInstance(id1)).Returns(inst1);
        _mockRegistry.Setup(r => r.CreateInstance(id2)).Returns(inst2);

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Modules =
            [
                new ConfiguredModule { ModuleId = id1 },
                new ConfiguredModule { ModuleId = id2 }
            ]
        };

        await _sessionManager.StartSessionAsync(preset);

        // Act
        var stoppedTcs = new TaskCompletionSource();
        _sessionManager.SessionStopped += _ => stoppedTcs.TrySetResult();
        await _sessionManager.StopCurrentSessionAsync();
        await stoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert
        module2Stopped.Should().BeTrue("Module 2 should still be stopped despite Module 1 failure");
        _sessionManager.IsSessionRunning.Should().BeFalse();
    }

    private static Mock<IModule> CreateMockModule()
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

    private static ModuleDefinition CreateModuleDefinition(Guid id, Type moduleType)
    {
        return new ModuleDefinition
        {
            Id = id,
            Name = "Test",
            Platforms = [Platform.Windows],
            ModuleType = moduleType
        };
    }

    private static (IModule Instance, ILifetimeScope Scope) CreateInstanceTuple(IModule module,
        ModuleDefinition definition)
    {
        var root = new ContainerBuilder().Build();
        var scope = root.BeginLifetimeScope(b => b.RegisterInstance(definition).As<ModuleDefinition>());
        return (module, scope);
    }
}