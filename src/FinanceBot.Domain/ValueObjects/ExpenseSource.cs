namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Источник, из которого получена категория траты.
/// Хранится как строка в app.expenses.source — используется для дебага и аналитики качества.
/// </summary>
public enum ExpenseSource
{
    Manual = 1,
    Memory = 2,
    Rules = 3,
    Claude = 4,
    Fallback = 5,
    RecurringAuto = 6,
    PlannedConfirmed = 7,
    CsvImport = 8
}

public static class ExpenseSourceExtensions
{
    public static string ToWireName(this ExpenseSource source) => source switch
    {
        ExpenseSource.Manual => "manual",
        ExpenseSource.Memory => "memory",
        ExpenseSource.Rules => "rules",
        ExpenseSource.Claude => "claude",
        ExpenseSource.Fallback => "fallback",
        ExpenseSource.RecurringAuto => "recurring-auto",
        ExpenseSource.PlannedConfirmed => "planned-confirmed",
        ExpenseSource.CsvImport => "csv-import",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };
}
