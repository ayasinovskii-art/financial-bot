using FinanceBot.Domain.Services;

namespace FinanceBot.Infrastructure.WorkdayCalendar;

/// <summary>
/// Простая реализация <see cref="IWorkdayCalendar"/>: пн–пт = рабочий день, сб/вс — выходные.
/// Праздники не учитываются. Используется как fallback и в тестах.
/// </summary>
public sealed class StaticWorkdayCalendar : IWorkdayCalendar
{
    public Task<bool> IsWorkdayAsync(DateOnly date, CancellationToken ct)
        => Task.FromResult(date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday);

    public async Task<DateOnly> NextWorkdayOnOrAfterAsync(DateOnly date, CancellationToken ct)
    {
        for (var d = date; ; d = d.AddDays(1))
        {
            if (await IsWorkdayAsync(d, ct).ConfigureAwait(false))
            {
                return d;
            }
        }
    }

    public async Task<DateOnly> PreviousWorkdayOnOrBeforeAsync(DateOnly date, CancellationToken ct)
    {
        for (var d = date; ; d = d.AddDays(-1))
        {
            if (await IsWorkdayAsync(d, ct).ConfigureAwait(false))
            {
                return d;
            }
        }
    }
}
