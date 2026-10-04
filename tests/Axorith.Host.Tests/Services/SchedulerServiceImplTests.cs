using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Axorith.Host.Tests.Services;

public class SchedulerServiceImplTests
{
    private readonly Mock<IScheduleManager> _mockScheduleManager;
    private readonly SchedulerServiceImpl _service;

    public SchedulerServiceImplTests()
    {
        _mockScheduleManager = new Mock<IScheduleManager>();
        _mockScheduleManager.Setup(m => m.ListSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SessionSchedule>());
        _mockScheduleManager.Setup(m => m.GetConfigurationLockStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Axorith.Core.Models.ConfigurationLockStatus(false, null));
        _service = new SchedulerServiceImpl(
            _mockScheduleManager.Object,
            NullLogger<SchedulerServiceImpl>.Instance
        );
    }



    [Fact]
    public async Task CreateSchedule_WithNullSchedule_ShouldThrowRpcException()
    {
        var request = new CreateScheduleRequest { Schedule = null };
        var context = GrpcTestContext.Create();

        Func<Task> act = async () => await _service.CreateSchedule(request, context);

        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }



    [Fact]
    public async Task UpdateSchedule_WithNullSchedule_ShouldThrowRpcException()
    {
        var request = new UpdateScheduleRequest { Schedule = null };
        var context = GrpcTestContext.Create();

        Func<Task> act = async () => await _service.UpdateSchedule(request, context);

        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }



    [Fact]
    public async Task DeleteSchedule_WithInvalidId_ShouldThrowRpcException()
    {
        var request = new DeleteScheduleRequest { ScheduleId = "invalid-guid" };
        var context = GrpcTestContext.Create();

        Func<Task> act = async () => await _service.DeleteSchedule(request, context);

        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }



    [Fact]
    public async Task SetEnabled_WithInvalidId_ShouldThrowRpcException()
    {
        var request = new SetScheduleEnabledRequest
        {
            ScheduleId = "invalid-guid",
            Enabled = true
        };
        var context = GrpcTestContext.Create();

        Func<Task> act = async () => await _service.SetEnabled(request, context);

        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task SetEnabled_WhenScheduleNotFound_ShouldThrowNotFoundRpcException()
    {
        var scheduleId = Guid.NewGuid();
        _mockScheduleManager.Setup(m => m.SetEnabledAsync(scheduleId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionSchedule?)null);

        var request = new SetScheduleEnabledRequest
        {
            ScheduleId = scheduleId.ToString(),
            Enabled = true
        };
        var context = GrpcTestContext.Create();

        Func<Task> act = async () => await _service.SetEnabled(request, context);

        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.NotFound);
    }

}
