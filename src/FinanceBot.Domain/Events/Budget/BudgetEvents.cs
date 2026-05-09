using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Events.Budget;

/// <summary>Открыт новый бюджетный период (либо первый, либо после закрытия предыдущего).</summary>
public sealed record BudgetPeriodStarted(
    Guid UserId,
    Guid PeriodId,
    DateOnly StartDate,
    PeriodType PeriodType,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Аллокация бюджета пересчитана (после поступления дохода).</summary>
public sealed record BudgetAllocated(
    Guid UserId,
    Guid PeriodId,
    decimal TotalIncome,
    decimal AllocationEssentials,
    decimal AllocationFun,
    decimal AllocationDeposit,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Достигнут заданный порог утилизации бакета (например, 80%).</summary>
public sealed record BucketThresholdCrossed(
    Guid UserId,
    Guid PeriodId,
    Bucket Bucket,
    decimal ThresholdRatio,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Пользователь подтвердил фактический перевод на накопления.</summary>
public sealed record SavingsReported(
    Guid UserId,
    Guid PeriodId,
    decimal Amount,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>Период бюджета закрыт. Summary — сериализованный финальный отчёт.</summary>
public sealed record BudgetPeriodClosed(
    Guid UserId,
    Guid PeriodId,
    DateOnly EndDate,
    string SummaryJson,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
