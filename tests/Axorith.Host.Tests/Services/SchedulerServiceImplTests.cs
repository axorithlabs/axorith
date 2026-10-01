using Axorith.Contracts;
using Axorith.Core.Models;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
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

    private static ServerCallContext CreateTestContext()
    {
        return TestServerCallContext.Create(
            method: "test",
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

    #region ListSchedules Tests

    #endregion

    #region CreateSchedule Tests

    [Fact]
    public async Task CreateSchedule_WithNullSchedule_ShouldThrowRpcException()
    {
        // Arrange
        var request = new CreateScheduleRequest { Schedule = null };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.CreateSchedule(request, context);

        // Assert - missing required data is invalid input.
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    #endregion

    #region UpdateSchedule Tests

    [Fact]
    public async Task UpdateSchedule_WithNullSchedule_ShouldThrowRpcException()
    {
        // Arrange
        var request = new UpdateScheduleRequest { Schedule = null };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.UpdateSchedule(request, context);

        // Assert - missing required data is invalid input.
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    #endregion

    #region DeleteSchedule Tests

    [Fact]
    public async Task DeleteSchedule_WithInvalidId_ShouldThrowRpcException()
    {
        // Arrange
        var request = new DeleteScheduleRequest { ScheduleId = "invalid-guid" };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.DeleteSchedule(request, context);

        // Assert - malformed IDs are invalid input.
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    #endregion

    #region SetEnabled Tests

    [Fact]
    public async Task SetEnabled_WithInvalidId_ShouldThrowRpcException()
    {
        // Arrange
        var request = new SetScheduleEnabledRequest
        {
            ScheduleId = "invalid-guid",
            Enabled = true
        };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.SetEnabled(request, context);

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task SetEnabled_WhenScheduleNotFound_ShouldThrowNotFoundRpcException()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        _mockScheduleManager.Setup(m => m.SetEnabledAsync(scheduleId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionSchedule?)null);

        var request = new SetScheduleEnabledRequest
        {
            ScheduleId = scheduleId.ToString(),
            Enabled = true
        };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.SetEnabled(request, context);

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.NotFound);
    }

    #endregion
}
