using FinanceBot.Application.Projections;
using FinanceBot.Domain.ValueObjects;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Projections;

public sealed class ExpenseReadModelWriter(IDbContextFactory<AppDbContext> dbFactory) : IExpenseReadModelWriter
{
    public async Task InsertReportedAsync(
        Guid expenseId, Guid userId, Guid periodId, DateTimeOffset occurredAt,
        decimal amount, string description, ExpenseSource source, DateTimeOffset createdAt, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Expenses.FindAsync([expenseId], ct);
        if (existing is not null)
        {
            return;
        }

        db.Expenses.Add(new ExpenseEntity
        {
            ExpenseId = expenseId,
            UserId = userId,
            PeriodId = periodId,
            OccurredAt = occurredAt,
            Amount = amount,
            Description = description,
            Category = Category.Other.ToString(),
            Bucket = Bucket.Essentials.ToString(),
            Source = source.ToWireName(),
            NeedsReview = source == ExpenseSource.Manual,
            AutoConfirmed = false,
            TemplateId = null,
            PlannedId = null,
            CreatedAt = createdAt
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateCategoryAsync(
        Guid expenseId, Category newCategory, Bucket newBucket, ExpenseSource source, bool needsReview, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Expenses.FindAsync([expenseId], ct);
        if (existing is null)
        {
            return;
        }
        existing.Category = newCategory.ToString();
        existing.Bucket = newBucket.ToString();
        existing.Source = source.ToWireName();
        existing.NeedsReview = needsReview;
        await db.SaveChangesAsync(ct);
    }
}
