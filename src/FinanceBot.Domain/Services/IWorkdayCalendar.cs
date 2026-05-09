namespace FinanceBot.Domain.Services;

/// <summary>
/// Календарь рабочих дней с учётом региональных праздников.
/// </summary>
public interface IWorkdayCalendar
{
    /// <summary>Является ли дата рабочим днём.</summary>
    Task<bool> IsWorkdayAsync(DateOnly date, CancellationToken ct);

    /// <summary>Ближайший рабочий день, начиная с указанного (включительно).</summary>
    Task<DateOnly> NextWorkdayOnOrAfterAsync(DateOnly date, CancellationToken ct);

    /// <summary>Ближайший рабочий день, заканчивая указанным (включительно).</summary>
    Task<DateOnly> PreviousWorkdayOnOrBeforeAsync(DateOnly date, CancellationToken ct);
}
