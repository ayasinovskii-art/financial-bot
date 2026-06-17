namespace FinanceBot.Domain.Events.Import;

/// <summary>
/// Итог массового импорта выписки. Сами траты/доходы фиксируются отдельными событиями
/// (ExpenseReported / IncomeReported); это событие — сводка для аудита и аналитики.
/// </summary>
public sealed record StatementImported(
    Guid UserId,
    int ImportedCount,
    int SkippedDuplicates,
    int FailedCount,
    decimal ExpenseTotal,
    decimal IncomeTotal,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
