using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Events.Expense;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Projections;

public sealed class ExpenseProjection(
    IProjectionOffsetStore offsetStore,
    IExpenseReadModelWriter writer,
    ICategoryBucketMap bucketMap)
    : ProjectionBase(offsetStore)
{
    public const string Name = "expenses";

    protected override string ProjectionName => Name;
    protected override string Tag => PersistenceTags.Expense;

    protected override Task HandleAsync(object payload, CancellationToken ct) => payload switch
    {
        ExpenseReported r => writer.InsertReportedAsync(
            r.ExpenseId, r.UserId, r.PeriodId, r.OccurredAt, r.Amount, r.Description, r.Source, DateTimeOffset.UtcNow, ct),
        ExpenseCategorizedAutomatically c => writer.UpdateCategoryAsync(
            c.ExpenseId, c.Category, bucketMap.Map(c.Category), c.Source, c.NeedsReview, ct),
        ExpenseCategoryCorrected cc => writer.UpdateCategoryAsync(
            cc.ExpenseId, cc.NewCategory, bucketMap.Map(cc.NewCategory), ExpenseSource.Manual, false, ct),
        ExpenseDeleted d => writer.DeleteAsync(d.UserId, d.ExpenseId, ct),
        _ => Task.CompletedTask
    };
}

public sealed class ExpenseProjectionMarker;
