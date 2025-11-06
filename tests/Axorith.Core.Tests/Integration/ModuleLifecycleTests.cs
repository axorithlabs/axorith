using Autofac;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Axorith.Core.Tests.Integration;

/// <summary>
///     Integration tests for module lifecycle (discovery -> initialization -> session -> disposal)
/// </summary>
public class ModuleLifecycleTests
{
    [Fact]
    public async Task FullModuleLifecycle_ShouldExecuteInCorrectOrder()
    {
        // Arrange
        var executionLog = new List<string>();

        var mockModule = new Mock<IModule>();
        mockModule.Setup(m => m.GetSettings()).Returns([]);
        mockModule.Setup(m => m.GetActions()).Returns([]);

        mockModule.Setup(m => m.InitializeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => executionLog.Add("Initialize"))
            .Returns(Task.CompletedTask);

        mockModule.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => executionLog.Add("Validate"))
            .ReturnsAsync(ValidationResult.Success);

        mockModule.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => executionLog.Add("SessionStart"))
            .Returns(Task.CompletedTask);

        mockModule.Setup(m => m.OnSessionEndAsync())
            .Callback(() => executionLog.Add("SessionEnd"))
            .Returns(Task.CompletedTask);

        mockModule.Setup(m => m.Dispose())
            .Callback(() => executionLog.Add("Dispose"));

        var mockRegistry = new Mock<IModuleRegistry>();
        var moduleId = Guid.NewGuid();
        var definition = new ModuleDefinition
        {
            Id = moduleId,
            Name = "Test Module",
            Platforms = [Platform.Windows],
            ModuleType = mockModule.Object.GetType()
        };
        var root = new ContainerBuilder().Build();
        var scope = root.BeginLifetimeScope(b => b.RegisterInstance(definition).As<ModuleDefinition>());
        mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((mockModule.Object, scope));

        var sessionManager = new SessionManager(mockRegistry.Object, NullLogger<SessionManager>.Instance);

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test Preset",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        // Act
        await sessionManager.StartSessionAsync(preset);
        await sessionManager.StopCurrentSessionAsync();
        await sessionManager.DisposeAsync();

        // Assert
        executionLog.Should().ContainInOrder(
            "Validate",
            "SessionStart",
            "SessionEnd"
        );
    }

    [Fact]
    public async Task ModuleWithSettings_ShouldApplyConfigurationFromPreset()
    {
        // Arrange
        var textSetting = Setting.AsText("name", "Name", "default");
        var numberSetting = Setting.AsInt("count", "Count", 0);

        var mockModule = new Mock<IModule>();
        mockModule.Setup(m => m.GetSettings()).Returns([textSetting, numberSetting]);
        mockModule.Setup(m => m.GetActions()).Returns([]);
        mockModule.Setup(m => m.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockModule.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success);
        mockModule.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockRegistry = new Mock<IModuleRegistry>();
        var moduleId = Guid.NewGuid();
        var definition = new ModuleDefinition
        {
            Id = moduleId,
            Name = "Config Module",
            Platforms = [Platform.Windows],
            ModuleType = mockModule.Object.GetType()
        };
        var root = new ContainerBuilder().Build();
        var scope = root.BeginLifetimeScope(b => b.RegisterInstance(definition).As<ModuleDefinition>());
        mockRegistry.Setup(r => r.CreateInstance(moduleId)).Returns((mockModule.Object, scope));

        var sessionManager = new SessionManager(mockRegistry.Object, NullLogger<SessionManager>.Instance);

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Config Test",
            Modules =
            [
                new ConfiguredModule
                {
                    ModuleId = moduleId,
                    Settings = new Dictionary<string, string>
                    {
                        ["name"] = "Custom Name",
                        ["count"] = "42"
                    }
                }
            ]
        };

        // Act
        await sessionManager.StartSessionAsync(preset);

        // Assert
        textSetting.GetCurrentValue().Should().Be("Custom Name");
        numberSetting.GetCurrentValue().Should().Be(42);
    }

    [Fact]
    public async Task MultipleModules_ShouldAllBeInitializedAndStarted()
    {
        // Arrange
        var module1Started = false;
        var module2Started = false;

        var mockModule1 = new Mock<IModule>();
        mockModule1.Setup(m => m.GetSettings()).Returns([]);
        mockModule1.Setup(m => m.GetActions()).Returns([]);
        mockModule1.Setup(m => m.InitializeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _ = true)
            .Returns(Task.CompletedTask);
        mockModule1.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success);
        mockModule1.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => module1Started = true)
            .Returns(Task.CompletedTask);

        var mockModule2 = new Mock<IModule>();
        mockModule2.Setup(m => m.GetSettings()).Returns([]);
        mockModule2.Setup(m => m.GetActions()).Returns([]);
        mockModule2.Setup(m => m.InitializeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _ = true)
            .Returns(Task.CompletedTask);
        mockModule2.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success);
        mockModule2.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => module2Started = true)
            .Returns(Task.CompletedTask);

        var mockRegistry = new Mock<IModuleRegistry>();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var definition1 = new ModuleDefinition
        {
            Id = id1, Name = "Module 1", Platforms = [Platform.Windows],
            ModuleType = mockModule1.Object.GetType()
        };
        var definition2 = new ModuleDefinition
        {
            Id = id2, Name = "Module 2", Platforms = [Platform.Windows],
            ModuleType = mockModule2.Object.GetType()
        };
        var root = new ContainerBuilder().Build();
        var scope1 = root.BeginLifetimeScope(b => b.RegisterInstance(definition1).As<ModuleDefinition>());
        var scope2 = root.BeginLifetimeScope(b => b.RegisterInstance(definition2).As<ModuleDefinition>());
        mockRegistry.Setup(r => r.CreateInstance(id1)).Returns((mockModule1.Object, scope1));
        mockRegistry.Setup(r => r.CreateInstance(id2)).Returns((mockModule2.Object, scope2));

        var sessionManager = new SessionManager(mockRegistry.Object, NullLogger<SessionManager>.Instance);

        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Multi Module Test",
            Modules =
            [
                new ConfiguredModule { ModuleId = id1 },
                new ConfiguredModule { ModuleId = id2 }
            ]
        };

        // Act
        await sessionManager.StartSessionAsync(preset);
        await Task.Delay(50);

        // Assert
        module1Started.Should().BeTrue();
        module2Started.Should().BeTrue();
    }
}