using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Projections;

/// <summary>Запись бюджетных периодов в app.periods.</summary>
public interface IPeriodReadModelWriter
{
    Task UpsertOnStartAsync(
        Guid periodId,
        Guid userId,
        DateOnly startDate,
        PeriodType type,
        CancellationToken ct);

    Task UpdateAllocationAsync(
        Guid periodId,
        decimal totalIncome,
        decimal allocationEssentials,
        decimal allocationFun,
        decimal allocationDeposit,
        CancellationToken ct);

    Task UpdateSavingsActualAsync(
        Guid periodId,
        decimal savingsActual,
        CancellationToken ct);

    Task CloseAsync(
        Guid periodId,
        DateOnly endDate,
        CancellationToken ct);
}
