using FinanceBot.Application.Projections;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Projections;

public sealed class IncomeReadModelWriter(IDbContextFactory<AppDbContext> dbFactory) : IIncomeReadModelWriter
{
    public async Task InsertAsync(
        Guid incomeId, Guid userId, Guid periodId, decimal amount, DateTimeOffset occurredAt,
        string? description, DateTimeOffset createdAt, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Incomes.FindAsync([incomeId], ct);
        if (existing is not null)
        {
            return; // idempotent
        }

        db.Incomes.Add(new IncomeEntity
        {
            IncomeId = incomeId,
            UserId = userId,
            PeriodId = periodId,
            Amount = amount,
            OccurredAt = occurredAt,
            Description = description,
            CreatedAt = createdAt
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid incomeId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Incomes
            .Where(i => i.IncomeId == incomeId)
            .ExecuteDeleteAsync(ct);
    }
}
