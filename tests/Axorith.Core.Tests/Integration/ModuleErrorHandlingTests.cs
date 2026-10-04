using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Core.Tests.Services;
using Axorith.Sdk;
using Axorith.Sdk.Settings;
using Axorith.Shared.Exceptions;
using FluentAssertions;
using Moq;

namespace Axorith.Core.Tests.Integration;

public class ModuleErrorHandlingTests
{
    [Fact]
    public async Task ModuleLifecycle_WithValidationError_ShouldPreventSessionStart()
    {
        var module = SessionManagerTestFactory.CreateModule();
        module.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Fail("Settings are invalid"));

        var registry = new Mock<IModuleRegistry>();
        var moduleId = SessionManagerTestFactory.Register(registry, module, name: "Invalid Module");
        var sessionManager = SessionManagerTestFactory.CreateManager(registry.Object);

        await sessionManager.Invoking(sm => sm.StartSessionAsync(SessionManagerTestFactory.CreatePreset(moduleId)))
            .Should()
            .ThrowAsync<SessionException>();
        sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ModuleLifecycle_WithSettingConfigurationError_ShouldBeDetected()
    {
        var requiredSetting = Setting.AsText("required", "Required", "");
        var module = SessionManagerTestFactory.CreateModule();
        module.Setup(m => m.GetSettings()).Returns([requiredSetting]);
        module.Setup(m => m.ValidateSettingsAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(string.IsNullOrWhiteSpace(requiredSetting.GetCurrentValue())
                ? ValidationResult.Fail("Required setting is empty")
                : ValidationResult.Success));

        var registry = new Mock<IModuleRegistry>();
        var moduleId = SessionManagerTestFactory.Register(registry, module, name: "Config Module");
        var sessionManager = SessionManagerTestFactory.CreateManager(registry.Object);

        await sessionManager.Invoking(sm => sm.StartSessionAsync(SessionManagerTestFactory.CreatePreset(moduleId)))
            .Should()
            .ThrowAsync<SessionException>();
        sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ModuleLifecycle_WithOnSessionEndException_ShouldStillCompleteStop()
    {
        var module = SessionManagerTestFactory.CreateModule();
        module.Setup(m => m.OnSessionEndAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Stop failed"));

        var registry = new Mock<IModuleRegistry>();
        var moduleId = SessionManagerTestFactory.Register(registry, module, name: "Error Stop");
        var sessionManager = SessionManagerTestFactory.CreateManager(registry.Object);

        await sessionManager.StartSessionAsync(SessionManagerTestFactory.CreatePreset(moduleId));
        await sessionManager.StopCurrentSessionAsync();

        sessionManager.IsSessionRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ModuleLifecycle_WithDisposeException_ShouldStillCompleteCleanup()
    {
        var disposed = false;
        var module = SessionManagerTestFactory.CreateModule();
        module.Setup(m => m.Dispose()).Callback(() =>
        {
            disposed = true;
            throw new InvalidOperationException("Dispose failed");
        });

        var registry = new Mock<IModuleRegistry>();
        var moduleId = SessionManagerTestFactory.Register(registry, module, name: "Error Dispose");
        var sessionManager = SessionManagerTestFactory.CreateManager(registry.Object);

        await sessionManager.StartSessionAsync(SessionManagerTestFactory.CreatePreset(moduleId));
        await sessionManager.StopCurrentSessionAsync();

        disposed.Should().BeTrue("Dispose should have been called");
        sessionManager.IsSessionRunning.Should().BeFalse();
    }
}
