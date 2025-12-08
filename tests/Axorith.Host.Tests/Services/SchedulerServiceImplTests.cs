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

    [Fact]
    public async Task ListSchedules_WithNoSchedules_ShouldReturnEmptyList()
    {
        // Arrange
        _mockScheduleManager.Setup(m => m.ListSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SessionSchedule>());

        var request = new ListSchedulesRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.ListSchedules(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSchedules_WithSchedules_ShouldReturnAll()
    {
        // Arrange
        var schedules = new List<SessionSchedule>
        {
            new()
            {
                Id = Guid.NewGuid(),
                PresetId = Guid.NewGuid(),
                Name = "Schedule 1",
                Type = ScheduleType.OneTime,
                OneTimeDate = DateTimeOffset.Now.AddDays(1)
            },
            new()
            {
                Id = Guid.NewGuid(),
                PresetId = Guid.NewGuid(),
                Name = "Schedule 2",
                Type = ScheduleType.Recurring,
                RecurringTime = TimeSpan.FromHours(9)
            }
        };

        _mockScheduleManager.Setup(m => m.ListSchedulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedules);

        var request = new ListSchedulesRequest();
        var context = CreateTestContext();

        // Act
        var response = await _service.ListSchedules(request, context);

        // Assert
        response.Schedules.Should().HaveCount(2);
        response.Schedules.Should().Contain(s => s.Name == "Schedule 1");
        response.Schedules.Should().Contain(s => s.Name == "Schedule 2");
    }

    #endregion

    #region CreateSchedule Tests

    [Fact]
    public async Task CreateSchedule_WithValidData_ShouldCreateSchedule()
    {
        // Arrange
        var presetId = Guid.NewGuid();
        var request = new CreateScheduleRequest
        {
            Schedule = new Schedule
            {
                PresetId = presetId.ToString(),
                Name = "New Schedule",
                IsEnabled = true,
                Type = 0 // OneTime
            }
        };

        _mockScheduleManager.Setup(m => m.SaveScheduleAsync(It.IsAny<SessionSchedule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionSchedule s, CancellationToken _) => s);

        var context = CreateTestContext();

        // Act
        var response = await _service.CreateSchedule(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Name.Should().Be("New Schedule");
        response.Id.Should().NotBeNullOrEmpty();

        _mockScheduleManager.Verify(
            m => m.SaveScheduleAsync(It.IsAny<SessionSchedule>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateSchedule_WithNullSchedule_ShouldThrowRpcException()
    {
        // Arrange
        var request = new CreateScheduleRequest { Schedule = null };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.CreateSchedule(request, context);

        // Assert - implementation wraps all exceptions as Internal
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.Internal);
    }

    #endregion

    #region UpdateSchedule Tests

    [Fact]
    public async Task UpdateSchedule_WithValidData_ShouldUpdateSchedule()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        var presetId = Guid.NewGuid();
        var request = new UpdateScheduleRequest
        {
            Schedule = new Schedule
            {
                Id = scheduleId.ToString(),
                PresetId = presetId.ToString(),
                Name = "Updated Schedule",
                IsEnabled = true,
                Type = 1 // Recurring
            }
        };

        _mockScheduleManager.Setup(m => m.SaveScheduleAsync(It.IsAny<SessionSchedule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionSchedule s, CancellationToken _) => s);

        var context = CreateTestContext();

        // Act
        var response = await _service.UpdateSchedule(request, context);

        // Assert
        response.Should().NotBeNull();
        response.Name.Should().Be("Updated Schedule");

        _mockScheduleManager.Verify(
            m => m.SaveScheduleAsync(It.IsAny<SessionSchedule>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSchedule_WithNullSchedule_ShouldThrowRpcException()
    {
        // Arrange
        var request = new UpdateScheduleRequest { Schedule = null };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.UpdateSchedule(request, context);

        // Assert - implementation wraps all exceptions as Internal
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.Internal);
    }

    #endregion

    #region DeleteSchedule Tests

    [Fact]
    public async Task DeleteSchedule_WithValidId_ShouldDeleteSchedule()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        _mockScheduleManager.Setup(m => m.DeleteScheduleAsync(scheduleId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new DeleteScheduleRequest { ScheduleId = scheduleId.ToString() };
        var context = CreateTestContext();

        // Act
        await _service.DeleteSchedule(request, context);

        // Assert
        _mockScheduleManager.Verify(m => m.DeleteScheduleAsync(scheduleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSchedule_WithInvalidId_ShouldThrowRpcException()
    {
        // Arrange
        var request = new DeleteScheduleRequest { ScheduleId = "invalid-guid" };
        var context = CreateTestContext();

        // Act
        Func<Task> act = async () => await _service.DeleteSchedule(request, context);

        // Assert - implementation wraps all exceptions as Internal
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.Internal);
    }

    #endregion

    #region SetEnabled Tests

    [Fact]
    public async Task SetEnabled_WithValidId_ShouldUpdateEnabled()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        var schedule = new SessionSchedule
        {
            Id = scheduleId,
            PresetId = Guid.NewGuid(),
            Name = "Test Schedule",
            IsEnabled = false
        };

        var enabledSchedule = new SessionSchedule
        {
            Id = scheduleId,
            PresetId = schedule.PresetId,
            Name = schedule.Name,
            IsEnabled = true
        };
        _mockScheduleManager.Setup(m => m.SetEnabledAsync(scheduleId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enabledSchedule);

        var request = new SetScheduleEnabledRequest
        {
            ScheduleId = scheduleId.ToString(),
            Enabled = true
        };
        var context = CreateTestContext();

        // Act
        var response = await _service.SetEnabled(request, context);

        // Assert
        response.Should().NotBeNull();
        response.IsEnabled.Should().BeTrue();

        _mockScheduleManager.Verify(m => m.SetEnabledAsync(scheduleId, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

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