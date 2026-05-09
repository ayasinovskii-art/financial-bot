namespace FinanceBot.Application.Scheduling;

/// <summary>
/// Утилиты расчёта пересечения локальной времени-точки в UTC-окне (from, to].
/// Используется SchedulerActor для system-tick детекторов вроде ClaudeAutoRecoveryTick (20:00 server).
/// </summary>
public static class ServerTimeWindow
{
    /// <summary>
    /// Возвращает true, если в полуоткрытом окне (<paramref name="from"/>, <paramref name="to"/>] есть момент,
    /// который в указанной таймзоне совпадает с <paramref name="hour"/>:<paramref name="minute"/>:00.
    /// Окно может покрывать разные локальные дни (TZ-сдвиги).
    /// </summary>
    public static bool ContainsLocalTimeOfDay(
        DateTimeOffset from, DateTimeOffset to, int hour, int minute, TimeZoneInfo timezone)
    {
        if (to <= from)
        {
            return false;
        }

        var localFrom = TimeZoneInfo.ConvertTime(from, timezone);
        var localTo = TimeZoneInfo.ConvertTime(to, timezone);

        for (var d = localFrom.Date; d <= localTo.Date; d = d.AddDays(1))
        {
            var localTarget = new DateTime(d.Year, d.Month, d.Day, hour, minute, 0, DateTimeKind.Unspecified);
            var fireAt = new DateTimeOffset(localTarget, timezone.GetUtcOffset(localTarget)).ToUniversalTime();
            if (fireAt > from && fireAt <= to)
            {
                return true;
            }
        }
        return false;
    }
}
