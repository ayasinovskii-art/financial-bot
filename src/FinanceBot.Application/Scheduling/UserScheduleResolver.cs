using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Scheduling;

/// <summary>
/// Pure-ish расчёт времени per-user тиков (UTC) с учётом TZ юзера и календаря рабочих дней.
/// Используется SchedulerActor'ом и Stage 18 wakeup-логикой для backfill.
/// </summary>
public interface IUserScheduleResolver
{
    Task<DateTimeOffset> NextEveningTickAsync(UserScheduleSettings s, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Возвращает ближайший SalaryDayTick (с учётом shift_rule). Null, если SalaryDays пуст.
    /// </summary>
    Task<DateTimeOffset?> NextSalaryDayTickAsync(UserScheduleSettings s, DateTimeOffset now, CancellationToken ct);

    /// <summary>Первый рабочий день недели в TZ юзера, evening_time? — нет, 09:00 по ТЗ §3.11.</summary>
    Task<DateTimeOffset> NextWeeklyAdvisorTickAsync(UserScheduleSettings s, DateTimeOffset now, CancellationToken ct);

    /// <summary>Первый рабочий день месяца в TZ юзера, 09:00 (см. §3.11).</summary>
    Task<DateTimeOffset> NextMonthlyAdvisorTickAsync(UserScheduleSettings s, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Перечисляет все salary-day-тики в окне [from, to] (включительно), с применённым shift_rule.
    /// Используется wakeup для перечисления пропущенных тиков.
    /// </summary>
    IAsyncEnumerable<DateTimeOffset> EnumerateSalaryDayTicksAsync(
        UserScheduleSettings s, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed class UserScheduleResolver : IUserScheduleResolver
{
    private static readonly TimeOfDay AdvisorMorning = TimeOfDay.Morning;

    private readonly IWorkdayCalendar _calendar;

    public UserScheduleResolver(IWorkdayCalendar calendar)
    {
        _calendar = calendar;
    }

    public Task<DateTimeOffset> NextEveningTickAsync(UserScheduleSettings s, DateTimeOffset now, CancellationToken ct)
    {
        // Evening-тик не привязан к рабочему дню — каждый день по ТЗ.
        var local = TimeZoneInfo.ConvertTime(now, s.Timezone);
        var todayAt = ComposeLocal(local.Date, s.EveningTime, s.Timezone);
        var next = todayAt > now ? todayAt : ComposeLocal(local.Date.AddDays(1), s.EveningTime, s.Timezone);
        return Task.FromResult(next);
    }

    public async Task<DateTimeOffset?> NextSalaryDayTickAsync(UserScheduleSettings s, DateTimeOffset now, CancellationToken ct)
    {
        if (s.SalaryDays.Count == 0)
        {
            return null;
        }

        var local = TimeZoneInfo.ConvertTime(now, s.Timezone);
        // Ищем в текущем месяце, потом в следующем — проходим до 2 месяцев гарантированно.
        for (var monthOffset = 0; monthOffset <= 2; monthOffset++)
        {
            var year = local.Year;
            var month = local.Month + monthOffset;
            while (month > 12) { month -= 12; year += 1; }

            foreach (var rawDay in s.SalaryDays.OrderBy(d => d))
            {
                var daysInMonth = DateTime.DaysInMonth(year, month);
                if (rawDay > daysInMonth)
                {
                    continue;
                }
                var rawDate = new DateOnly(year, month, rawDay);
                var shifted = await ApplyShiftAsync(rawDate, s.ShiftRule, ct).ConfigureAwait(false);
                var fireAt = ComposeLocal(shifted.ToDateTime(TimeOnly.MinValue), s.EveningTime, s.Timezone);
                if (fireAt > now)
                {
                    return fireAt;
                }
            }
        }
        return null;
    }

    public async Task<DateTimeOffset> NextWeeklyAdvisorTickAsync(UserScheduleSettings s, DateTimeOffset now, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(now, s.Timezone);
        // Понедельник этой недели; если уже после понедельника или сегодняшний 09:00 прошёл — следующая.
        var monday = StartOfWeek(local.Date);
        for (var weekOffset = 0; weekOffset <= 4; weekOffset++)
        {
            var weekStart = DateOnly.FromDateTime(monday).AddDays(weekOffset * 7);
            var firstWorkday = await _calendar.NextWorkdayOnOrAfterAsync(weekStart, ct).ConfigureAwait(false);
            // Гарантируем, что не выйдем за пределы недели — если первый рабочий день > пятницы недели, берём в любом случае.
            var fireAt = ComposeLocal(firstWorkday.ToDateTime(TimeOnly.MinValue), AdvisorMorning, s.Timezone);
            if (fireAt > now)
            {
                return fireAt;
            }
        }
        // Defensive fallback (никогда не должно случиться при корректном календаре).
        return ComposeLocal(local.Date.AddDays(7), AdvisorMorning, s.Timezone);
    }

    public async Task<DateTimeOffset> NextMonthlyAdvisorTickAsync(UserScheduleSettings s, DateTimeOffset now, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(now, s.Timezone);
        for (var monthOffset = 0; monthOffset <= 2; monthOffset++)
        {
            var year = local.Year;
            var month = local.Month + monthOffset;
            while (month > 12) { month -= 12; year += 1; }
            var firstOfMonth = new DateOnly(year, month, 1);
            var firstWorkday = await _calendar.NextWorkdayOnOrAfterAsync(firstOfMonth, ct).ConfigureAwait(false);
            var fireAt = ComposeLocal(firstWorkday.ToDateTime(TimeOnly.MinValue), AdvisorMorning, s.Timezone);
            if (fireAt > now)
            {
                return fireAt;
            }
        }
        return ComposeLocal(local.Date.AddMonths(1), AdvisorMorning, s.Timezone);
    }

    public async IAsyncEnumerable<DateTimeOffset> EnumerateSalaryDayTicksAsync(
        UserScheduleSettings s, DateTimeOffset from, DateTimeOffset to,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (s.SalaryDays.Count == 0)
        {
            yield break;
        }

        var localFrom = TimeZoneInfo.ConvertTime(from, s.Timezone);
        var localTo = TimeZoneInfo.ConvertTime(to, s.Timezone);
        var startMonth = new DateOnly(localFrom.Year, localFrom.Month, 1);
        var endMonth = new DateOnly(localTo.Year, localTo.Month, 1);

        for (var m = startMonth; m <= endMonth; m = m.AddMonths(1))
        {
            foreach (var rawDay in s.SalaryDays.OrderBy(d => d))
            {
                var daysInMonth = DateTime.DaysInMonth(m.Year, m.Month);
                if (rawDay > daysInMonth)
                {
                    continue;
                }
                var rawDate = new DateOnly(m.Year, m.Month, rawDay);
                var shifted = await ApplyShiftAsync(rawDate, s.ShiftRule, ct).ConfigureAwait(false);
                var fireAt = ComposeLocal(shifted.ToDateTime(TimeOnly.MinValue), s.EveningTime, s.Timezone);
                if (fireAt >= from && fireAt <= to)
                {
                    yield return fireAt;
                }
            }
        }
    }

    private async Task<DateOnly> ApplyShiftAsync(DateOnly raw, ShiftRule rule, CancellationToken ct)
    {
        if (rule == ShiftRule.None)
        {
            return raw;
        }
        var isWorkday = await _calendar.IsWorkdayAsync(raw, ct).ConfigureAwait(false);
        if (isWorkday)
        {
            return raw;
        }
        return rule switch
        {
            ShiftRule.Previous => await _calendar.PreviousWorkdayOnOrBeforeAsync(raw.AddDays(-1), ct).ConfigureAwait(false),
            ShiftRule.Next => await _calendar.NextWorkdayOnOrAfterAsync(raw.AddDays(1), ct).ConfigureAwait(false),
            _ => raw
        };
    }

    private static DateTimeOffset ComposeLocal(DateTime localDate, TimeOfDay time, TimeZoneInfo tz)
    {
        var localDt = new DateTime(localDate.Year, localDate.Month, localDate.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(localDt);
        return new DateTimeOffset(localDt, offset).ToUniversalTime();
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff).Date;
    }
}
