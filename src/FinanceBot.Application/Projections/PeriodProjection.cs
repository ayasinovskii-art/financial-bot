using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Events.Budget;

namespace FinanceBot.Application.Projections;

public sealed class PeriodProjection(IProjectionOffsetStore offsetStore, IPeriodReadModelWriter writer)
    : ProjectionBase(offsetStore)
{
    public const string Name = "periods";

    protected override string ProjectionName => Name;
    protected override string Tag => PersistenceTags.Period;

    protected override Task HandleAsync(object payload, CancellationToken ct) => payload switch
    {
        BudgetPeriodStarted s => writer.UpsertOnStartAsync(s.PeriodId, s.UserId, s.StartDate, s.PeriodType, ct),
        BudgetAllocated a => writer.UpdateAllocationAsync(
            a.PeriodId, a.TotalIncome, a.AllocationEssentials, a.AllocationFun, a.AllocationDeposit, ct),
        SavingsReported sa => writer.UpdateSavingsActualAsync(sa.PeriodId, sa.Amount, ct),
        BudgetPeriodClosed c => writer.CloseAsync(c.PeriodId, c.EndDate, ct),
        _ => Task.CompletedTask
    };
}

public sealed class PeriodProjectionMarker;
