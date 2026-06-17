namespace FinanceBot.Domain.Events.Income;

/// <summary>Зафиксирован доход.</summary>
public sealed record IncomeReported(
    Guid UserId,
    Guid IncomeId,
    Guid PeriodId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    string? Description,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Бот спросил пользователя о доходе (для аналитики, опционально). Не меняет состояние.</summary>
public sealed record IncomeReportRequested(
    Guid UserId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Удаление дохода (компенсирующее событие).</summary>
public sealed record IncomeDeleted(
    Guid UserId,
    Guid IncomeId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
