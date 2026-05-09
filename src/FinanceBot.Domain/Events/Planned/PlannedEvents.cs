namespace FinanceBot.Domain.Events.Planned;

/// <summary>Запланирована разовая трата на конкретную дату.</summary>
public sealed record PlannedExpenseAdded(
    Guid UserId,
    Guid PlannedId,
    decimal Amount,
    DateOnly Date,
    string Description,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Запланированная трата подтверждена (создаётся ExpenseReported в UserActor).</summary>
public sealed record PlannedExpenseConfirmed(
    Guid UserId,
    Guid PlannedId,
    Guid ExpenseId,
    decimal ActualAmount,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Запланированная трата отменена пользователем.</summary>
public sealed record PlannedExpenseCancelled(
    Guid UserId,
    Guid PlannedId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
