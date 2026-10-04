using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Core.Tests.Services;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
using FluentAssertions;
using Moq;

namespace Axorith.Core.Tests.Integration;

public class ModuleLifecycleTests
{
    [Fact]
    public async Task FullModuleLifecycle_ShouldExecuteInCorrectOrder()
    {
        var executionLog = new List<string>();
        var module = SessionManagerTestFactory.CreateModule();
        module.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => executionLog.Add("Validate"))
            .ReturnsAsync(ValidationResult.Success);
        module.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => executionLog.Add("SessionStart"))
            .Returns(Task.CompletedTask);
        module.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()))
            .Callback(() => executionLog.Add("SessionEnd"))
            .Returns(Task.CompletedTask);
        module.Setup(m => m.Dispose()).Callback(() => executionLog.Add("Dispose"));

        var registry = new Mock<IModuleRegistry>();
        var moduleId = SessionManagerTestFactory.Register(registry, module, name: "Test Module");
        var sessionManager = SessionManagerTestFactory.CreateManager(registry.Object);
        var preset = new SessionPreset
        {
            Id = Guid.NewGuid(),
            Name = "Test Preset",
            Modules = [new ConfiguredModule { ModuleId = moduleId }]
        };

        await sessionManager.StartSessionAsync(preset);
        await sessionManager.StopCurrentSessionAsync();
        await sessionManager.DisposeAsync();

        executionLog.Should().ContainInOrder("Validate", "SessionStart", "SessionEnd", "Dispose");
    }

    [Fact]
    public async Task ModuleWithSettings_ShouldApplyConfigurationFromPreset()
    {
        var textSetting = Setting.AsText("name", "Name", "default");
        var numberSetting = Setting.AsInt("count", "Count", 0);
        var module = SessionManagerTestFactory.CreateModule();
        module.Setup(m => m.GetSettings()).Returns([textSetting, numberSetting]);

        var registry = new Mock<IModuleRegistry>();
        var moduleId = SessionManagerTestFactory.Register(registry, module, name: "Config Module");
        var sessionManager = SessionManagerTestFactory.CreateManager(registry.Object);
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

        await sessionManager.StartSessionAsync(preset);

        textSetting.GetCurrentValue().Should().Be("Custom Name");
        numberSetting.GetCurrentValue().Should().Be(42);
    }

    [Fact]
    public async Task MultipleModules_ShouldAllBeInitializedAndStarted()
    {
        var module1Started = false;
        var module2Started = false;
        var module1 = SessionManagerTestFactory.CreateModule();
        var module2 = SessionManagerTestFactory.CreateModule();
        module1.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => module1Started = true)
            .Returns(Task.CompletedTask);
        module2.Setup(m => m.OnSessionStartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => module2Started = true)
            .Returns(Task.CompletedTask);

        var registry = new Mock<IModuleRegistry>();
        var id1 = SessionManagerTestFactory.Register(registry, module1, name: "Module 1");
        var id2 = SessionManagerTestFactory.Register(registry, module2, name: "Module 2");
        var sessionManager = SessionManagerTestFactory.CreateManager(registry.Object);
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

        await sessionManager.StartSessionAsync(preset);

        module1Started.Should().BeTrue();
        module2Started.Should().BeTrue();
    }
}
