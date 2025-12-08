using Axorith.Core.Models;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Sdk.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Axorith.Core.Tests.Services;

public class ScheduleManagerTests : IAsyncDisposable
{
    private readonly string _testStorageDirectory;
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<IPresetManager> _mockPresetManager;
    private readonly Mock<ISessionAutoStopService> _mockAutoStopService;
    private readonly Mock<INotifier> _mockNotifier;
    private readonly ScheduleManager _manager;

    public ScheduleManagerTests()
    {
        _testStorageDirectory = Path.Combine(Path.GetTempPath(), $"axorith-schedule-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testStorageDirectory);

        _mockSessionManager = new Mock<ISessionManager>();
        _mockPresetManager = new Mock<IPresetManager>();
        _mockAutoStopService = new Mock<ISessionAutoStopService>();
        _mockNotifier = new Mock<INotifier>();

        _manager = new ScheduleManager(
            _testStorageDirectory,
            _mockSessionManager.Object,
            _mockPresetManager.Object,
            _mockAutoStopService.Object,
            _mockNotifier.Object,
            NullLogger<ScheduleManager>.Instance
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync();

        if (Directory.Exists(_testStorageDirectory))
        {
            Directory.Delete(_testStorageDirectory, recursive: true);
        }
    }

    #region ListSchedules Tests

    [Fact]
    public async Task ListSchedulesAsync_WithNoSchedules_ShouldReturnEmptyList()
    {
        // Act
        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);

        // Assert
        schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSchedulesAsync_WithSchedules_ShouldReturnAll()
    {
        // Arrange
        var schedule1 = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = Guid.NewGuid(),
            Name = "Schedule 1",
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddDays(1)
        };

        var schedule2 = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = Guid.NewGuid(),
            Name = "Schedule 2",
            Type = ScheduleType.Recurring,
            RecurringTime = TimeSpan.FromHours(9)
        };

        await _manager.SaveScheduleAsync(schedule1, CancellationToken.None);
        await _manager.SaveScheduleAsync(schedule2, CancellationToken.None);

        // Act
        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);

        // Assert
        schedules.Should().HaveCount(2);
        schedules.Should().Contain(s => s.Name == "Schedule 1");
        schedules.Should().Contain(s => s.Name == "Schedule 2");
    }

    #endregion

    #region SaveSchedule Tests

    [Fact]
    public async Task SaveScheduleAsync_NewSchedule_ShouldAddToList()
    {
        // Arrange
        var schedule = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = Guid.NewGuid(),
            Name = "Test Schedule",
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddHours(1)
        };

        // Act
        var saved = await _manager.SaveScheduleAsync(schedule, CancellationToken.None);

        // Assert
        saved.Should().NotBeNull();
        saved.Name.Should().Be("Test Schedule");

        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);
        schedules.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveScheduleAsync_ExistingSchedule_ShouldUpdate()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        var schedule = new SessionSchedule
        {
            Id = scheduleId,
            PresetId = Guid.NewGuid(),
            Name = "Original Name",
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddHours(1)
        };

        await _manager.SaveScheduleAsync(schedule, CancellationToken.None);

        var updatedSchedule = new SessionSchedule
        {
            Id = scheduleId,
            PresetId = schedule.PresetId,
            Name = "Updated Name",
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddHours(2)
        };

        // Act
        await _manager.SaveScheduleAsync(updatedSchedule, CancellationToken.None);

        // Assert
        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);
        schedules.Should().HaveCount(1);
        schedules[0].Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task SaveScheduleAsync_ShouldPersistToDisk()
    {
        // Arrange
        var schedule = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = Guid.NewGuid(),
            Name = "Persistent Schedule",
            Type = ScheduleType.Recurring,
            RecurringTime = TimeSpan.FromHours(14)
        };

        // Act
        await _manager.SaveScheduleAsync(schedule, CancellationToken.None);

        // Assert
        var filePath = Path.Combine(_testStorageDirectory, "schedules.json");
        File.Exists(filePath).Should().BeTrue();
    }

    #endregion

    #region DeleteSchedule Tests

    [Fact]
    public async Task DeleteScheduleAsync_ExistingSchedule_ShouldRemove()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        var schedule = new SessionSchedule
        {
            Id = scheduleId,
            PresetId = Guid.NewGuid(),
            Name = "To Delete",
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddHours(1)
        };

        await _manager.SaveScheduleAsync(schedule, CancellationToken.None);

        // Act
        await _manager.DeleteScheduleAsync(scheduleId, CancellationToken.None);

        // Assert
        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);
        schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteScheduleAsync_NonExistentSchedule_ShouldNotThrow()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var act = async () => await _manager.DeleteScheduleAsync(nonExistentId, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region SetEnabled Tests

    [Fact]
    public async Task SetEnabledAsync_ExistingSchedule_ShouldUpdateEnabled()
    {
        // Arrange
        var scheduleId = Guid.NewGuid();
        var schedule = new SessionSchedule
        {
            Id = scheduleId,
            PresetId = Guid.NewGuid(),
            Name = "Toggle Test",
            IsEnabled = true,
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddHours(1)
        };

        await _manager.SaveScheduleAsync(schedule, CancellationToken.None);

        // Act
        var result = await _manager.SetEnabledAsync(scheduleId, false, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.IsEnabled.Should().BeFalse();

        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);
        schedules[0].IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetEnabledAsync_NonExistentSchedule_ShouldReturnNull()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _manager.SetEnabledAsync(nonExistentId, true, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Start/Stop Tests

    [Fact]
    public async Task StartAsync_ShouldLoadSchedulesFromDisk()
    {
        // Arrange - First save a schedule and dispose
        var schedule = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = Guid.NewGuid(),
            Name = "Persistent",
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddDays(1)
        };

        await _manager.SaveScheduleAsync(schedule, CancellationToken.None);
        await _manager.DisposeAsync();

        // Create a new manager instance
        var newManager = new ScheduleManager(
            _testStorageDirectory,
            _mockSessionManager.Object,
            _mockPresetManager.Object,
            _mockAutoStopService.Object,
            _mockNotifier.Object,
            NullLogger<ScheduleManager>.Instance
        );

        try
        {
            // Act
            await newManager.StartAsync(CancellationToken.None);

            // Assert
            var schedules = await newManager.ListSchedulesAsync(CancellationToken.None);
            schedules.Should().HaveCount(1);
            schedules[0].Name.Should().Be("Persistent");
        }
        finally
        {
            await newManager.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_ShouldCleanupResources()
    {
        // Arrange
        await _manager.StartAsync(CancellationToken.None);

        // Act
        var act = async () => await _manager.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion
}

public class SessionScheduleTests
{
    #region GetNextRun Tests

    [Fact]
    public void GetNextRun_DisabledSchedule_ShouldReturnNull()
    {
        // Arrange
        var schedule = new SessionSchedule
        {
            IsEnabled = false,
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddHours(1)
        };

        // Act
        var result = schedule.GetNextRun(DateTimeOffset.Now);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetNextRun_OneTime_FutureDate_ShouldReturnDate()
    {
        // Arrange
        var futureDate = DateTimeOffset.Now.AddHours(2);
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.OneTime,
            OneTimeDate = futureDate
        };

        // Act
        var result = schedule.GetNextRun(DateTimeOffset.Now);

        // Assert
        result.Should().Be(futureDate);
    }

    [Fact]
    public void GetNextRun_OneTime_PastDate_ShouldReturnNull()
    {
        // Arrange
        var pastDate = DateTimeOffset.Now.AddHours(-2);
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.OneTime,
            OneTimeDate = pastDate
        };

        // Act
        var result = schedule.GetNextRun(DateTimeOffset.Now);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetNextRun_Recurring_NoTimeSet_ShouldReturnNull()
    {
        // Arrange
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.Recurring,
            RecurringTime = null
        };

        // Act
        var result = schedule.GetNextRun(DateTimeOffset.Now);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetNextRun_Recurring_WithTime_ShouldReturnNextOccurrence()
    {
        // Arrange
        var now = DateTimeOffset.Now;
        var futureTime = now.TimeOfDay.Add(TimeSpan.FromHours(1));
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.Recurring,
            RecurringTime = futureTime
        };

        // Act
        var result = schedule.GetNextRun(now);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TimeOfDay.Should().BeCloseTo(futureTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetNextRun_Recurring_WithDayFilter_ShouldRespectDays()
    {
        // Arrange
        var now = DateTimeOffset.Now;
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.Recurring,
            RecurringTime = TimeSpan.FromHours(10),
            DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]
        };

        // Act
        var result = schedule.GetNextRun(now);

        // Assert
        if (result.HasValue)
        {
            schedule.DaysOfWeek.Should().Contain(result.Value.DayOfWeek);
        }
    }

    [Fact]
    public void GetNextRun_Recurring_EmptyDayFilter_ShouldRunEveryDay()
    {
        // Arrange - use a time before the recurring time to ensure same-day result
        var now = new DateTimeOffset(2024, 1, 15, 8, 0, 0, TimeSpan.Zero); // Monday 8 AM
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.Recurring,
            RecurringTime = TimeSpan.FromHours(10), // 10 AM - after 'now'
            DaysOfWeek = [] // Empty = every day
        };

        // Act
        var result = schedule.GetNextRun(now);

        // Assert
        result.Should().NotBeNull();
        // The next run should be today at 10 AM (since now is 8 AM and recurring time is 10 AM)
        result!.Value.TimeOfDay.Should().Be(TimeSpan.FromHours(10));
    }

    #endregion
}