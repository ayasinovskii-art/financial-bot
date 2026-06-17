namespace FinanceBot.Domain.Events.Goal;

/// <summary>Пользователь добавил финансовую цель.</summary>
public sealed record GoalAdded(
    Guid UserId,
    Guid GoalId,
    string Description,
    decimal? TargetAmount,
    DateOnly? TargetDate,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Пользователь отметил цель достигнутой.</summary>
public sealed record GoalCompleted(
    Guid UserId,
    Guid GoalId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Удаление цели (компенсирующее событие).</summary>
public sealed record GoalRemoved(
    Guid UserId,
    Guid GoalId,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
