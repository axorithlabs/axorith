using Axorith.Contracts;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using Axorith.Host.Streaming;
using Axorith.Sdk;
using Axorith.Sdk.Actions;
using Axorith.Sdk.Settings;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;

namespace Axorith.Host.Tests.Services;

public class ModulesServiceImplTests
{
    private readonly Mock<IModuleRegistry> _mockModuleRegistry;
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly ModulesServiceImpl _service;

    public ModulesServiceImplTests()
    {
        _mockModuleRegistry = new Mock<IModuleRegistry>();
        _mockSessionManager = new Mock<ISessionManager>();
        var broadcaster = new SettingUpdateBroadcaster(
            _mockSessionManager.Object,
            NullLogger<SettingUpdateBroadcaster>.Instance,
            Options.Create(new Configuration())
        );

        var sandboxManager = new DesignTimeSandboxManager(
            _mockModuleRegistry.Object,
            broadcaster,
            NullLogger<DesignTimeSandboxManager>.Instance,
            Options.Create(new Configuration()),
            _mockSessionManager.Object);

        _service = new ModulesServiceImpl(
            _mockModuleRegistry.Object,
            _mockSessionManager.Object,
            broadcaster,
            sandboxManager,
            NullLogger<ModulesServiceImpl>.Instance
        );
    }

    [Fact]
    public async Task GetModuleSettings_WithInvalidGuid_ShouldThrowRpcException()
    {
        var request = new GetModuleSettingsRequest { ModuleId = "invalid-guid" };
        var context = GrpcTestContext.Create();

        Func<Task> act = async () => await _service.GetModuleSettings(request, context);

        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task GetModuleSettings_WhenModuleNotFound_ShouldThrowRpcException()
    {
        var moduleId = Guid.NewGuid();
        _mockModuleRegistry.Setup(m => m.GetDefinitionById(moduleId)).Returns((ModuleDefinition?)null);

        var request = new GetModuleSettingsRequest { ModuleId = moduleId.ToString() };
        var context = GrpcTestContext.Create();

        Func<Task> act = async () => await _service.GetModuleSettings(request, context);

        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.NotFound);
    }



    [Fact]
    public async Task InvokeAction_WithInvalidGuid_ShouldReturnFailure()
    {
        var request = new InvokeActionRequest
        {
            ModuleInstanceId = "invalid-guid",
            ActionKey = "test-action"
        };
        var context = GrpcTestContext.Create();

        var response = await _service.InvokeAction(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Invalid module instance ID");
    }

    [Fact]
    public async Task InvokeAction_WhenModuleNotFound_ShouldReturnFailure()
    {
        var instanceId = Guid.NewGuid();
        _mockSessionManager.Setup(m => m.GetActiveModuleInstanceByInstanceId(instanceId))
            .Returns((IModule?)null);

        var request = new InvokeActionRequest
        {
            ModuleInstanceId = instanceId.ToString(),
            ActionKey = "test-action"
        };
        var context = GrpcTestContext.Create();

        var response = await _service.InvokeAction(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Module instance is not active");
    }

    [Fact]
    public async Task InvokeAction_WhenActionNotFound_ShouldReturnFailure()
    {
        var instanceId = Guid.NewGuid();
        var mockModule = new Mock<IModule>();
        mockModule.Setup(m => m.GetActions()).Returns(new List<IAction>());
        _mockSessionManager.Setup(m => m.GetActiveModuleInstanceByInstanceId(instanceId))
            .Returns(mockModule.Object);

        var request = new InvokeActionRequest
        {
            ModuleInstanceId = instanceId.ToString(),
            ActionKey = "non-existent-action"
        };
        var context = GrpcTestContext.Create();

        var response = await _service.InvokeAction(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Action not found");
    }



    [Fact]
    public async Task UpdateSetting_WithInvalidGuid_ShouldReturnFailure()
    {
        var request = new UpdateSettingRequest
        {
            ModuleInstanceId = "invalid-guid",
            SettingKey = "test-setting",
            StringValue = "test-value"
        };
        var context = GrpcTestContext.Create();

        var response = await _service.UpdateSetting(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Invalid module instance ID");
    }

    [Fact]
    public async Task UpdateSetting_WhenSettingNotFound_ShouldReturnFailure()
    {
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
        var context = GrpcTestContext.Create();

        var response = await _service.UpdateSetting(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Setting not found");
    }

    [Fact]
    public async Task UpdateSetting_WhenModuleNotRunningAndNoSandbox_ShouldReturnFailure()
    {
        var instanceId = Guid.NewGuid();
        _mockSessionManager.Setup(m => m.GetActiveModuleInstanceByInstanceId(instanceId)).Returns((IModule?)null);

        var request = new UpdateSettingRequest
        {
            ModuleInstanceId = instanceId.ToString(),
            SettingKey = "test-setting",
            StringValue = "value"
        };
        var context = GrpcTestContext.Create();

        var response = await _service.UpdateSetting(request, context);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("No active module or design-time sandbox");
    }


}
