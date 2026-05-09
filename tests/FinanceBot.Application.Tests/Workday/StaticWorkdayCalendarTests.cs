using FinanceBot.Infrastructure.WorkdayCalendar;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Workday;

public sealed class StaticWorkdayCalendarTests
{
    private readonly StaticWorkdayCalendar _calendar = new();

    [Theory]
    [InlineData(2026, 5, 11, true)]  // Monday
    [InlineData(2026, 5, 13, true)]  // Wednesday
    [InlineData(2026, 5, 15, true)]  // Friday
    [InlineData(2026, 5, 16, false)] // Saturday
    [InlineData(2026, 5, 17, false)] // Sunday
    public async Task IsWorkday_returns_expected(int year, int month, int day, bool expected)
    {
        var result = await _calendar.IsWorkdayAsync(new DateOnly(year, month, day), CancellationToken.None);
        result.Should().Be(expected);
    }

    [Fact]
    public async Task NextWorkdayOnOrAfter_skips_weekend()
    {
        // Saturday 2026-05-16 → next workday Monday 2026-05-18.
        var next = await _calendar.NextWorkdayOnOrAfterAsync(new DateOnly(2026, 5, 16), CancellationToken.None);
        next.Should().Be(new DateOnly(2026, 5, 18));
    }

    [Fact]
    public async Task PreviousWorkdayOnOrBefore_skips_weekend()
    {
        // Sunday 2026-05-17 → previous workday Friday 2026-05-15.
        var prev = await _calendar.PreviousWorkdayOnOrBeforeAsync(new DateOnly(2026, 5, 17), CancellationToken.None);
        prev.Should().Be(new DateOnly(2026, 5, 15));
    }
}
