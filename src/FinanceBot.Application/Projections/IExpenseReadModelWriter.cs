using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Projections;

public interface IExpenseReadModelWriter
{
    Task InsertReportedAsync(
        Guid expenseId,
        Guid userId,
        Guid periodId,
        DateTimeOffset occurredAt,
        decimal amount,
        string description,
        ExpenseSource source,
        DateTimeOffset createdAt,
        CancellationToken ct);

    Task UpdateCategoryAsync(
        Guid expenseId,
        Category newCategory,
        Bucket newBucket,
        ExpenseSource source,
        bool needsReview,
        CancellationToken ct);

    Task DeleteAsync(Guid userId, Guid expenseId, CancellationToken ct);
}
