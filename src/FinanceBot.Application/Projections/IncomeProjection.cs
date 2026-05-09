using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Events.Income;

namespace FinanceBot.Application.Projections;

public sealed class IncomeProjection(IProjectionOffsetStore offsetStore, IIncomeReadModelWriter writer)
    : ProjectionBase(offsetStore)
{
    public const string Name = "incomes";

    protected override string ProjectionName => Name;
    protected override string Tag => PersistenceTags.Income;

    protected override Task HandleAsync(object payload, CancellationToken ct) => payload switch
    {
        IncomeReported i => writer.InsertAsync(
            i.IncomeId, i.UserId, i.PeriodId, i.Amount, i.OccurredAt, i.Description, DateTimeOffset.UtcNow, ct),
        _ => Task.CompletedTask
    };
}

public sealed class IncomeProjectionMarker;
