using FinanceBot.Application.Actors.Scheduler;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Scheduling;

/// <summary>
/// Unit-тесты для SchedulerActor.CrossesLocalSunday — детектор попадания воскресного 09:00 local в UTC-окно (from, to].
/// </summary>
public sealed class CrossesLocalSundayTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private const int DigestHour = SchedulerActor.WeeklyDigestHour; // 9

    // 2026-06-14 is a Sunday in UTC.
    private static readonly DateTimeOffset Sunday0900Utc = new(2026, 6, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Returns_true_when_window_straddles_sunday_09_00()
    {
        var from = Sunday0900Utc.AddMinutes(-1);
        var to = Sunday0900Utc;
        SchedulerActor.CrossesLocalSunday(Utc, from, to, DigestHour).Should().BeTrue();
    }

    [Fact]
    public void Returns_false_when_sunday_09_00_is_before_window()
    {
        var from = Sunday0900Utc;          // from is exclusive
        var to = Sunday0900Utc.AddMinutes(10);
        SchedulerActor.CrossesLocalSunday(Utc, from, to, DigestHour).Should().BeFalse();
    }

    [Fact]
    public void Returns_false_when_window_covers_sunday_but_not_09_00()
    {
        // Window on Sunday, but entirely before 09:00.
        var from = Sunday0900Utc.AddHours(-3);  // 06:00 UTC
        var to = Sunday0900Utc.AddHours(-1);    // 08:00 UTC
        SchedulerActor.CrossesLocalSunday(Utc, from, to, DigestHour).Should().BeFalse();
    }

    [Fact]
    public void Returns_false_for_non_sunday_window()
    {
        // 2026-06-15 is a Monday.
        var mondayNoon = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var from = mondayNoon.AddMinutes(-1);
        var to = mondayNoon;
        SchedulerActor.CrossesLocalSunday(Utc, from, to, DigestHour).Should().BeFalse();
    }

    [Fact]
    public void Detects_sunday_in_user_timezone_when_utc_crosses_midnight()
    {
        // Europe/Moscow = UTC+3.  Sunday 09:00 MSK == Saturday 06:00 UTC.
        // 2026-06-14 09:00 MSK == 2026-06-14 06:00 UTC.
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var sunday0900Msk = new DateTimeOffset(2026, 6, 14, 6, 0, 0, TimeSpan.Zero); // == 09:00 MSK

        var from = sunday0900Msk.AddMinutes(-1);
        var to = sunday0900Msk;
        SchedulerActor.CrossesLocalSunday(moscow, from, to, DigestHour).Should().BeTrue();
    }

    [Fact]
    public void Returns_false_for_inverted_window()
    {
        var from = Sunday0900Utc.AddHours(1);
        var to = Sunday0900Utc.AddHours(-1);
        SchedulerActor.CrossesLocalSunday(Utc, from, to, DigestHour).Should().BeFalse();
    }
}
