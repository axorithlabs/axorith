using System.Globalization;
using System.Reflection;
using Axorith.Client.ViewModels;
using Axorith.Core.Models;
using Axorith.Core.Services;
using Xunit;

namespace Axorith.Integrations.Tests;

public sealed class CommittedEndTimeTests
{
    [Fact]
    public void FixedEndUsesTheSelectedDateOffsetAcrossDaylightSavingTime()
    {
        var resolveEndAt = typeof(SessionManager).GetMethod("ResolveEndAt",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(TimeOnly), typeof(IReadOnlyCollection<DayOfWeek>), typeof(DateTimeOffset), typeof(TimeZoneInfo)])!;
        var eastern = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");

        var afterSpringForward = (DateTimeOffset)resolveEndAt.Invoke(null,
        [new TimeOnly(18, 0), new[] { DayOfWeek.Sunday },
            new DateTimeOffset(2024, 3, 9, 19, 0, 0, TimeSpan.FromHours(-5)), eastern])!;
        Assert.Equal(new DateTimeOffset(2024, 3, 10, 22, 0, 0, TimeSpan.Zero), afterSpringForward);

        var skippedLocalTime = (DateTimeOffset)resolveEndAt.Invoke(null,
        [new TimeOnly(2, 30), new[] { DayOfWeek.Sunday },
            new DateTimeOffset(2024, 3, 10, 1, 0, 0, TimeSpan.FromHours(-5)), eastern])!;
        Assert.Equal(new DateTimeOffset(2024, 3, 10, 7, 0, 0, TimeSpan.Zero), skippedLocalTime);

        var repeatedLocalTime = (DateTimeOffset)resolveEndAt.Invoke(null,
        [new TimeOnly(1, 30), new[] { DayOfWeek.Sunday },
            new DateTimeOffset(2024, 11, 3, 0, 0, 0, TimeSpan.FromHours(-4)), eastern])!;
        Assert.Equal(new DateTimeOffset(2024, 11, 3, 5, 30, 0, TimeSpan.Zero), repeatedLocalTime);
    }

    [Fact]
    public void StartReviewShowsTheConcreteEndDateForSelectedWeekdays()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var format = typeof(MainViewModel).GetMethod("FormatEndAtReview",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var options = new FocusCommitmentOptions
            {
                EndAtLocalTime = new TimeOnly(18, 0),
                EndAtDaysOfWeek = [DayOfWeek.Friday]
            };

            var result = (string)format.Invoke(null, [options, new DateTime(2024, 1, 1, 19, 0, 0)])!;

            Assert.Equal("Ends Fri, Jan 5 at 18:00 local time", result);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
