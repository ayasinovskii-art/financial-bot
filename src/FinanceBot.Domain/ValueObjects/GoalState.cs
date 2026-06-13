namespace FinanceBot.Domain.ValueObjects;

/// <summary>Снимок финансовой цели пользователя (in-memory, часть UserState).</summary>
public sealed record GoalState(
    Guid GoalId,
    string Description,
    decimal? TargetAmount,
    DateOnly? TargetDate,
    bool IsCompleted);
