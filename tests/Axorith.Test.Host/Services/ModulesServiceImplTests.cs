using Autofac;
using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host;
using Axorith.Host.Services;
using Axorith.Host.Streaming;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;

namespace Axorith.Test.Host.Services;

public class ModulesServiceImplTests
{
    private readonly Mock<IModuleRegistry> _mockModuleRegistry;
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly ModulesServiceImpl _service;

    public ModulesServiceImplTests()
    {
        _mockModuleRegistry = new Mock<IModuleRegistry>();
        _mockSessionManager = new Mock<ISessionManager>();
        var mockPresetManager = new Mock<IPresetManager>();

        // Create real broadcaster with mocked dependencies
        var broadcaster = new SettingUpdateBroadcaster(
            _mockSessionManager.Object,
            NullLogger<SettingUpdateBroadcaster>.Instance,
            Options.Create(new HostConfiguration())
        );

        var sandboxManager = new DesignTimeSandboxManager(
            _mockModuleRegistry.Object,
            broadcaster,
            NullLogger<DesignTimeSandboxManager>.Instance,
            Options.Create(new HostConfiguration()));

        _service = new ModulesServiceImpl(
            _mockModuleRegistry.Object,
            _mockSessionManager.Object,
            broadcaster,
            sandboxManager,
            NullLogger<ModulesServiceImpl>.Instance
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

    #region ListModules Tests

    [Fact]
    public async Task ListModules_WhenNoModules_ShouldReturnEmptyList()
    {
        // Arrange
        _mockModuleRegistry.Setup(m => m.GetAllDefinitions()).Returns(new List<ModuleDefinition>());
        var request = new ListModulesRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.ListModules(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Modules.Should().BeEmpty();
    }

    [Fact]
    public async Task ListModules_WithModules_ShouldReturnAll()
    {
        // Arrange
        var modules = new List<ModuleDefinition>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Module 1",
                Description = "Test Module 1",
                Category = "Test"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Module 2",
                Description = "Test Module 2",
                Category = "Test"
            }
        };

        _mockModuleRegistry.Setup(m => m.GetAllDefinitions()).Returns(modules);
        var request = new ListModulesRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.ListModules(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Modules.Should().HaveCount(2);
        response.Modules[0].Name.Should().Be("Module 1");
        response.Modules[1].Name.Should().Be("Module 2");
    }

    #endregion

    #region GetModuleSettings Tests

    [Fact]
    public async Task GetModuleSettings_WithInvalidGuid_ShouldThrowRpcException()
    {
        // Arrange
        var request = new GetModuleSettingsRequest { ModuleId = "invalid-guid" };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.GetModuleSettings(request, context);

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task GetModuleSettings_WhenModuleNotFound_ShouldThrowRpcException()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        _mockModuleRegistry.Setup(m => m.GetDefinitionById(moduleId)).Returns((ModuleDefinition?)null);

        var request = new GetModuleSettingsRequest { ModuleId = moduleId.ToString() };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.GetModuleSettings(request, context);

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.NotFound);
    }

    [Fact]
    public async Task GetModuleSettings_WhenModuleExists_ShouldReturnSettingsAndActions()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var moduleDefinition = new ModuleDefinition
        {
            Id = moduleId,
            Name = "Test Module",
            Description = "Test",
            Category = "Test",
            ModuleType = typeof(TestModule)
        };

        var mockModule = new Mock<IModule>();
        mockModule.Setup(m => m.GetSettings()).Returns(new List<ISetting>());
        mockModule.Setup(m => m.GetActions()).Returns(new List<IAction>());
        mockModule.Setup(m => m.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var mockScope = new Mock<ILifetimeScope>();

        _mockModuleRegistry.Setup(m => m.GetDefinitionById(moduleId)).Returns(moduleDefinition);
        _mockModuleRegistry.Setup(m => m.CreateInstance(moduleId)).Returns((mockModule.Object, mockScope.Object));

        var request = new GetModuleSettingsRequest { ModuleId = moduleId.ToString() };
        var context = CreateTestContext();

        // Act
        var response = await _service.GetModuleSettings(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Settings.Should().NotBeNull();
        response.Actions.Should().NotBeNull();

        mockModule.Verify(m => m.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockModule.Verify(m => m.Dispose(), Times.Once);
        mockScope.Verify(s => s.Dispose(), Times.Once);
    }

    #endregion

    #region InvokeAction Tests

    [Fact]
    public async Task InvokeAction_WithInvalidGuid_ShouldReturnFailure()
    {
        // Arrange
        var request = new InvokeActionRequest
        {
            ModuleInstanceId = "invalid-guid",
            ActionKey = "test-action"
        };
        var context = CreateTestContext();

        // Act
        var response = await _service.InvokeAction(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Invalid module ID");
    }

    [Fact]
    public async Task InvokeAction_WhenModuleNotFound_ShouldReturnFailure()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        _mockModuleRegistry.Setup(m => m.CreateInstance(moduleId)).Returns((null, null));

        var request = new InvokeActionRequest
        {
            ModuleInstanceId = moduleId.ToString(),
            ActionKey = "test-action"
        };
        var context = CreateTestContext();

        // Act
        var response = await _service.InvokeAction(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Module not found");
    }

    [Fact]
    public async Task InvokeAction_WhenActionNotFound_ShouldReturnFailure()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockModule = new Mock<IModule>();
        mockModule.Setup(m => m.GetActions()).Returns(new List<IAction>());
        var mockScope = new Mock<ILifetimeScope>();

        _mockModuleRegistry.Setup(m => m.CreateInstance(moduleId)).Returns((mockModule.Object, mockScope.Object));

        var request = new InvokeActionRequest
        {
            ModuleInstanceId = moduleId.ToString(),
            ActionKey = "non-existent-action"
        };
        var context = CreateTestContext();

        // Act
        var response = await _service.InvokeAction(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Action not found");

        mockModule.Verify(m => m.Dispose(), Times.Once);
        mockScope.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task InvokeAction_WhenActionExists_ShouldInvokeAndReturnSuccess()
    {
        // Arrange
        var moduleId = Guid.NewGuid();
        var mockAction = new Mock<IAction>();
        mockAction.Setup(a => a.Key).Returns("test-action");
        mockAction.Setup(a => a.InvokeAsync()).Returns(Task.CompletedTask);

        var mockModule = new Mock<IModule>();
        mockModule.Setup(m => m.GetActions()).Returns(new List<IAction> { mockAction.Object });
        var mockScope = new Mock<ILifetimeScope>();

        _mockModuleRegistry.Setup(m => m.CreateInstance(moduleId)).Returns((mockModule.Object, mockScope.Object));

        var request = new InvokeActionRequest
        {
            ModuleInstanceId = moduleId.ToString(),
            ActionKey = "test-action"
        };
        var context = CreateTestContext();

        // Act
        var response = await _service.InvokeAction(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Message.Should().Contain("completed successfully");

        mockAction.Verify(a => a.InvokeAsync(), Times.Once);
        mockModule.Verify(m => m.Dispose(), Times.Once);
        mockScope.Verify(s => s.Dispose(), Times.Once);
    }

    #endregion

    #region UpdateSetting Tests

    [Fact]
    public async Task UpdateSetting_WithInvalidGuid_ShouldReturnFailure()
    {
        // Arrange
        var request = new UpdateSettingRequest
        {
            ModuleInstanceId = "invalid-guid",
            SettingKey = "test-setting",
            StringValue = "test-value"
        };
        var context = CreateTestContext();

        // Act
        var response = await _service.UpdateSetting(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Invalid module instance ID");
    }

    [Fact]
    public async Task UpdateSetting_WhenModuleRunning_ShouldUpdateAndBroadcast()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        var mockSetting = new Mock<ISetting>();
        mockSetting.Setup(s => s.Key).Returns("test-setting");
        mockSetting.Setup(s => s.SetValueFromString(It.IsAny<string?>())).Verifiable();

        var mockModule = new Mock<IModule>();
        mockModule.Setup(m => m.GetSettings()).Returns(new List<ISetting> { mockSetting.Object });

        _mockSessionManager.Setup(m => m.GetActiveModuleInstanceByInstanceId(instanceId)).Returns(mockModule.Object);

        var request = new UpdateSettingRequest
        {
            ModuleInstanceId = instanceId.ToString(),
            SettingKey = "test-setting",
            StringValue = "new-value"
        };
        var context = CreateTestContext();

        // Act
        var response = await _service.UpdateSetting(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Message.Should().Contain("updated successfully");

        mockSetting.Verify(s => s.SetValueFromString("new-value"), Times.Once);
    }

    [Fact]
    public async Task UpdateSetting_WhenSettingNotFound_ShouldReturnFailure()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        var mockModule = new Mock<IModule>();
        mockModule.Setup(m => m.GetSettings()).Returns(new List<ISetting>());

        _mockSessionManager.Setup(m => m.GetActiveModuleInstanceByInstanceId(instanceId)).Returns(mockModule.Object);

        var request = new UpdateSettingRequest
        {
            ModuleInstanceId = instanceId.ToString(),
            SettingKey = "non-existent-setting",
            StringValue = "value"
        };
        var context = CreateTestContext();

        // Act
        var response = await _service.UpdateSetting(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Setting not found");
    }

    [Fact]
    public async Task UpdateSetting_WhenModuleNotRunning_ShouldBroadcastOnly()
    {
        // Arrange
        var instanceId = Guid.NewGuid();
        _mockSessionManager.Setup(m => m.GetActiveModuleInstanceByInstanceId(instanceId)).Returns((IModule?)null);

        var request = new UpdateSettingRequest
        {
            ModuleInstanceId = instanceId.ToString(),
            SettingKey = "test-setting",
            StringValue = "value"
        };
        var context = CreateTestContext();

        // Act
        var response = await _service.UpdateSetting(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Message.Should().Contain("broadcasted");
    }

    #endregion

    // Test module class for GetModuleSettings test
    private class TestModule : IModule
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task OnSessionStartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public static Task OnActionAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task OnSessionEndAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<ValidationResult> ValidateSettingsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ValidationResult.Success);
        }

        public static Task CleanupAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public IReadOnlyList<ISetting> GetSettings()
        {
            return new List<ISetting>();
        }

        public IReadOnlyList<IAction> GetActions()
        {
            return new List<IAction>();
        }

        public object GetSettingsViewModel()
        {
            return null!;
        }

        public Type? CustomSettingsViewType => null;
    }
}