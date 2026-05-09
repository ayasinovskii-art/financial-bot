namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Тип бюджетного периода. По умолчанию SalaryCycle.
/// </summary>
public enum PeriodType
{
    /// <summary>От поступления до поступления (по умолчанию).</summary>
    SalaryCycle = 1,
    /// <summary>Календарный месяц (1-е число — конец месяца).</summary>
    CalendarMonth = 2
}
