using FinanceBot.Application.Actors.Telegram;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Persistence;

public sealed class DeleteListReader(IDbContextFactory<AppDbContext> dbFactory) : IDeleteListReader
{
    public async Task<IReadOnlyList<DeleteExpenseRow>> GetLastExpensesAsync(
        Guid userId, int skip, int take, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.OccurredAt)
            .Skip(skip)
            .Take(take)
            .Select(e => new DeleteExpenseRow(e.ExpenseId, e.Amount, e.Description, e.OccurredAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DeleteIncomeRow>> GetLastIncomesAsync(
        Guid userId, int skip, int take, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Incomes
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.OccurredAt)
            .Skip(skip)
            .Take(take)
            .Select(i => new DeleteIncomeRow(i.IncomeId, i.Amount, i.Description, i.OccurredAt))
            .ToListAsync(ct);
    }

    public async Task<DeleteExpenseRow?> GetExpenseAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Expenses
            .AsNoTracking()
            .Where(e => e.ExpenseId == id && e.UserId == userId)
            .Select(e => new DeleteExpenseRow(e.ExpenseId, e.Amount, e.Description, e.OccurredAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<DeleteIncomeRow?> GetIncomeAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Incomes
            .AsNoTracking()
            .Where(i => i.IncomeId == id && i.UserId == userId)
            .Select(i => new DeleteIncomeRow(i.IncomeId, i.Amount, i.Description, i.OccurredAt))
            .FirstOrDefaultAsync(ct);
    }
}
