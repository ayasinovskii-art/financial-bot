namespace FinanceBot.Domain.Events.Reports;

/// <summary>Тип запрошенного графика.</summary>
public enum ChartType
{
    CategoryPie = 1,
    DailyBar = 2,
    BucketUtilization = 3,
    SavingsLine = 4
}

/// <summary>Запрошен график.</summary>
public sealed record ChartRequested(
    Guid UserId,
    ChartType ChartType,
    string? Params,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;

/// <summary>График сгенерирован и отправлен.</summary>
public sealed record ChartGenerated(
    Guid UserId,
    ChartType ChartType,
    long SizeBytes,
    DateTimeOffset OccurredAt,
    int EventVersion = 1) : IUserScopedEvent;
