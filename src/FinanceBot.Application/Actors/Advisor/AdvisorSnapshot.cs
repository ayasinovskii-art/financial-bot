using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Advisor;

/// <summary>
/// Снимок финансовой картины пользователя на момент построения совета. Иммутабельный.
/// Источники — read-model app.periods / app.expenses / app.incomes + live UserActor state (цели).
/// </summary>
public sealed record AdvisorSnapshot(
    Guid UserId,
    DateTimeOffset BuiltAt,
    PeriodSnapshot? CurrentPeriod,
    PeriodSnapshot? PreviousPeriod,
    IReadOnlyList<CategorySnapshot> CurrentByCategory,
    IReadOnlyList<CategorySnapshot> PreviousByCategory,
    IReadOnlyList<TopExpense> TopExpenses,
    int? DaysToEndOfPeriod,
    IReadOnlyDictionary<string, string> Settings)
{
    /// <summary>Активные (незавершённые) финансовые цели пользователя. Пусто если UserActor недоступен.</summary>
    public IReadOnlyList<GoalSnapshot> ActiveGoals { get; init; } = Array.Empty<GoalSnapshot>();
}

/// <summary>Иммутабельное DTO финансовой цели для включения в AdvisorSnapshot.</summary>
public sealed record GoalSnapshot(
    Guid GoalId,
    string Description,
    decimal? TargetAmount,
    DateOnly? TargetDate);

public sealed record PeriodSnapshot(
    Guid PeriodId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string Status,
    decimal TotalIncome,
    decimal AllocationEssentials,
    decimal AllocationFun,
    decimal AllocationDeposit,
    decimal SpentEssentials,
    decimal SpentFun,
    decimal SpentDeposit,
    decimal? SavingsActual);

public sealed record CategorySnapshot(Category Category, Bucket Bucket, decimal Spent, int Count);

public sealed record TopExpense(Guid ExpenseId, DateTimeOffset OccurredAt, decimal Amount, Category Category, string Description);
