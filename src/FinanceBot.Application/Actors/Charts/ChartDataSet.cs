using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Charts;

/// <summary>Базовый тип набора данных для рендеринга графика.</summary>
public abstract record ChartDataSet
{
    public required string Title { get; init; }
}

/// <summary>Pie chart по категориям за период.</summary>
public sealed record CategoryPieData : ChartDataSet
{
    public required IReadOnlyList<CategorySlice> Slices { get; init; }
}

public sealed record CategorySlice(Category Category, decimal Amount);

/// <summary>Bar chart дневных трат.</summary>
public sealed record DailyBarData : ChartDataSet
{
    public required IReadOnlyList<DailyBucket> Days { get; init; }
}

public sealed record DailyBucket(DateOnly Date, decimal Amount);

/// <summary>Stacked bar утилизации бакетов (одна группа из 3 баров с двумя сегментами spent/remaining).</summary>
public sealed record BucketUtilizationData : ChartDataSet
{
    public required decimal SpentEssentials { get; init; }
    public required decimal AllocationEssentials { get; init; }
    public required decimal SpentFun { get; init; }
    public required decimal AllocationFun { get; init; }
    public required decimal SpentDeposit { get; init; }
    public required decimal AllocationDeposit { get; init; }
}

/// <summary>Line chart прогресса накоплений по периодам.</summary>
public sealed record SavingsLineData : ChartDataSet
{
    public required IReadOnlyList<SavingsPoint> Points { get; init; }
}

public sealed record SavingsPoint(DateOnly PeriodStart, decimal Savings);
