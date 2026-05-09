using FinanceBot.Application.Scheduling;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Scheduling;

public sealed class UserScheduleResolverTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public async Task NextEveningTick_returns_today_evening_if_before()
    {
        var resolver = new UserScheduleResolver(new MonFriCalendar());
        var s = UserScheduleSettings.Default with { Timezone = Utc, EveningTime = new TimeOfDay(19, 0) };
        var now = new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero); // понедельник 10:00 UTC

        var next = await resolver.NextEveningTickAsync(s, now, default);
        next.Should().Be(new DateTimeOffset(2026, 5, 11, 19, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task NextEveningTick_rolls_to_tomorrow_if_after()
    {
        var resolver = new UserScheduleResolver(new MonFriCalendar());
        var s = UserScheduleSettings.Default with { Timezone = Utc, EveningTime = new TimeOfDay(19, 0) };
        var now = new DateTimeOffset(2026, 5, 11, 20, 0, 0, TimeSpan.Zero);

        var next = await resolver.NextEveningTickAsync(s, now, default);
        next.Should().Be(new DateTimeOffset(2026, 5, 12, 19, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task NextSalaryDayTick_with_previous_shift_uses_friday_when_25th_is_saturday()
    {
        // 2026-04-25 is Saturday. Previous → 2026-04-24 (Friday).
        var resolver = new UserScheduleResolver(new MonFriCalendar());
        var s = UserScheduleSettings.Default with
        {
            Timezone = Utc,
            EveningTime = new TimeOfDay(19, 0),
            SalaryDays = [25],
            ShiftRule = ShiftRule.Previous
        };
        var now = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);

        var next = await resolver.NextSalaryDayTickAsync(s, now, default);
        next!.Value.Should().Be(new DateTimeOffset(2026, 4, 24, 19, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task NextSalaryDayTick_with_next_shift_uses_monday_when_25th_is_sunday()
    {
        // 2025-05-25 is Sunday. Next → 2025-05-26 Monday.
        var resolver = new UserScheduleResolver(new MonFriCalendar());
        var s = UserScheduleSettings.Default with
        {
            Timezone = Utc,
            EveningTime = new TimeOfDay(19, 0),
            SalaryDays = [25],
            ShiftRule = ShiftRule.Next
        };
        var now = new DateTimeOffset(2025, 5, 20, 0, 0, 0, TimeSpan.Zero);

        var next = await resolver.NextSalaryDayTickAsync(s, now, default);
        next!.Value.Should().Be(new DateTimeOffset(2025, 5, 26, 19, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task NextWeeklyAdvisorTick_returns_first_workday_of_week()
    {
        var resolver = new UserScheduleResolver(new MonFriCalendar());
        var s = UserScheduleSettings.Default with { Timezone = Utc };
        var now = new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero); // Monday 10:00

        // Сегодня уже понедельник, 09:00 прошло — следующий тик на следующий понедельник 18 мая.
        var next = await resolver.NextWeeklyAdvisorTickAsync(s, now, default);
        next.Should().Be(new DateTimeOffset(2026, 5, 18, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task EnumerateSalaryDayTicks_yields_only_ticks_in_window()
    {
        var resolver = new UserScheduleResolver(new MonFriCalendar());
        var s = UserScheduleSettings.Default with
        {
            Timezone = Utc, EveningTime = new TimeOfDay(19, 0),
            SalaryDays = [10, 25], ShiftRule = ShiftRule.None
        };
        var from = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 5, 31, 23, 0, 0, TimeSpan.Zero);

        var list = new List<DateTimeOffset>();
        await foreach (var t in resolver.EnumerateSalaryDayTicksAsync(s, from, to, default))
        {
            list.Add(t);
        }
        list.Should().HaveCount(2);
        list[0].Should().Be(new DateTimeOffset(2026, 5, 10, 19, 0, 0, TimeSpan.Zero));
        list[1].Should().Be(new DateTimeOffset(2026, 5, 25, 19, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Простой Mon–Fri календарь без праздников — для детерминистичных тестов.
    /// </summary>
    private sealed class MonFriCalendar : IWorkdayCalendar
    {
        public Task<bool> IsWorkdayAsync(DateOnly date, CancellationToken ct)
            => Task.FromResult(date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday);

        public async Task<DateOnly> NextWorkdayOnOrAfterAsync(DateOnly date, CancellationToken ct)
        {
            while (!await IsWorkdayAsync(date, ct).ConfigureAwait(false))
            {
                date = date.AddDays(1);
            }
            return date;
        }

        public async Task<DateOnly> PreviousWorkdayOnOrBeforeAsync(DateOnly date, CancellationToken ct)
        {
            while (!await IsWorkdayAsync(date, ct).ConfigureAwait(false))
            {
                date = date.AddDays(-1);
            }
            return date;
        }
    }
}
