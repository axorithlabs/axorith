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


    [Fact]
    public async Task ListSchedulesAsync_WithNoSchedules_ShouldReturnEmptyList()
    {
        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);

        schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSchedulesAsync_WithSchedules_ShouldReturnAll()
    {
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

        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);

        schedules.Should().HaveCount(2);
        schedules.Should().Contain(s => s.Name == "Schedule 1");
        schedules.Should().Contain(s => s.Name == "Schedule 2");
    }



    [Fact]
    public async Task SaveScheduleAsync_NewSchedule_ShouldAddToList()
    {
        var schedule = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = Guid.NewGuid(),
            Name = "Test Schedule",
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddHours(1)
        };

        var saved = await _manager.SaveScheduleAsync(schedule, CancellationToken.None);

        saved.Should().NotBeNull();
        saved.Name.Should().Be("Test Schedule");

        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);
        schedules.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveScheduleAsync_ExistingSchedule_ShouldUpdate()
    {
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

        await _manager.SaveScheduleAsync(updatedSchedule, CancellationToken.None);

        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);
        schedules.Should().HaveCount(1);
        schedules[0].Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task SaveScheduleAsync_ShouldPersistToDisk()
    {
        var schedule = new SessionSchedule
        {
            Id = Guid.NewGuid(),
            PresetId = Guid.NewGuid(),
            Name = "Persistent Schedule",
            Type = ScheduleType.Recurring,
            RecurringTime = TimeSpan.FromHours(14)
        };

        await _manager.SaveScheduleAsync(schedule, CancellationToken.None);

        var filePath = Path.Combine(_testStorageDirectory, "config", "schedules.json");
        File.Exists(filePath).Should().BeTrue();
    }



    [Fact]
    public async Task DeleteScheduleAsync_ExistingSchedule_ShouldRemove()
    {
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

        await _manager.DeleteScheduleAsync(scheduleId, CancellationToken.None);

        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);
        schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteScheduleAsync_NonExistentSchedule_ShouldNotThrow()
    {
        var nonExistentId = Guid.NewGuid();

        var act = async () => await _manager.DeleteScheduleAsync(nonExistentId, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }



    [Fact]
    public async Task SetEnabledAsync_ExistingSchedule_ShouldUpdateEnabled()
    {
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

        var result = await _manager.SetEnabledAsync(scheduleId, false, CancellationToken.None);

        result.Should().NotBeNull();
        result.IsEnabled.Should().BeFalse();

        var schedules = await _manager.ListSchedulesAsync(CancellationToken.None);
        schedules[0].IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetEnabledAsync_NonExistentSchedule_ShouldReturnNull()
    {
        var nonExistentId = Guid.NewGuid();

        var result = await _manager.SetEnabledAsync(nonExistentId, true, CancellationToken.None);

        result.Should().BeNull();
    }



    [Fact]
    public async Task StartAsync_ShouldLoadSchedulesFromDisk()
    {
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
            await newManager.StartAsync(CancellationToken.None);

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
        await _manager.StartAsync(CancellationToken.None);

        var act = async () => await _manager.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

}

public class SessionScheduleTests
{

    [Fact]
    public void GetNextRun_DisabledSchedule_ShouldReturnNull()
    {
        var schedule = new SessionSchedule
        {
            IsEnabled = false,
            Type = ScheduleType.OneTime,
            OneTimeDate = DateTimeOffset.Now.AddHours(1)
        };

        var result = schedule.GetNextRun(DateTimeOffset.Now);

        result.Should().BeNull();
    }

    [Fact]
    public void GetNextRun_OneTime_FutureDate_ShouldReturnDate()
    {
        var futureDate = DateTimeOffset.Now.AddHours(2);
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.OneTime,
            OneTimeDate = futureDate
        };

        var result = schedule.GetNextRun(DateTimeOffset.Now);

        result.Should().Be(futureDate);
    }

    [Fact]
    public void GetNextRun_OneTime_PastDate_ShouldReturnNull()
    {
        var pastDate = DateTimeOffset.Now.AddHours(-2);
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.OneTime,
            OneTimeDate = pastDate
        };

        var result = schedule.GetNextRun(DateTimeOffset.Now);

        result.Should().BeNull();
    }

    [Fact]
    public void GetNextRun_Recurring_NoTimeSet_ShouldReturnNull()
    {
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.Recurring,
            RecurringTime = null
        };

        var result = schedule.GetNextRun(DateTimeOffset.Now);

        result.Should().BeNull();
    }

    [Fact]
    public void GetNextRun_Recurring_WithTime_ShouldReturnNextOccurrence()
    {
        var now = new DateTimeOffset(2024, 1, 15, 10, 0, 0,
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 1, 15, 10, 0, 0)));
        var futureTime = TimeSpan.FromHours(14);
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.Recurring,
            RecurringTime = futureTime
        };

        var result = schedule.GetNextRun(now);

        result.Should().NotBeNull();
        result.Value.Date.Should().Be(now.Date);
        result.Value.TimeOfDay.Should().Be(futureTime);
    }

    [Fact]
    public void GetNextRun_Recurring_WithDayFilter_ShouldRespectDays()
    {
        var now = DateTimeOffset.Now;
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.Recurring,
            RecurringTime = TimeSpan.FromHours(10),
            DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]
        };

        var result = schedule.GetNextRun(now);

        if (result.HasValue)
        {
            schedule.DaysOfWeek.Should().Contain(result.Value.DayOfWeek);
        }
    }

    [Fact]
    public void GetNextRun_Recurring_EmptyDayFilter_ShouldRunEveryDay()
    {
        var now = new DateTimeOffset(2024, 1, 15, 8, 0, 0, TimeSpan.Zero); // Monday 8 AM
        var schedule = new SessionSchedule
        {
            IsEnabled = true,
            Type = ScheduleType.Recurring,
            RecurringTime = TimeSpan.FromHours(10), // 10 AM - after 'now'
            DaysOfWeek = [] // Empty = every day
        };

        var result = schedule.GetNextRun(now);

        result.Should().NotBeNull();
        // The next run should be today at 10 AM (since now is 8 AM and recurring time is 10 AM)
        result.Value.TimeOfDay.Should().Be(TimeSpan.FromHours(10));
    }

}
