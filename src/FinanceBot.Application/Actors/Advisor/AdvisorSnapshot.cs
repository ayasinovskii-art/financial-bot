using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Advisor;

/// <summary>
/// Снимок финансовой картины пользователя на момент построения совета. Иммутабельный.
/// Источники — read-model app.periods / app.expenses / app.incomes.
/// </summary>
public sealed record AdvisorSnapshot(
    Guid UserId,
    DateTimeOffset BuiltAt,
    PeriodSnapshot? CurrentPeriod,
    PeriodSnapshot? PreviousPeriod,
    IReadOnlyList<CategorySnapshot> CurrentByCategory,
    IReadOnlyList<CategorySnapshot> PreviousByCategory,
    IReadOnlyList<TopExpense> TopExpenses,
    int? DaysToEndOfPeriod);

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
