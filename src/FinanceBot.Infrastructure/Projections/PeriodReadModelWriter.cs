using FinanceBot.Application.Projections;
using FinanceBot.Domain.ValueObjects;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Projections;

public sealed class PeriodReadModelWriter(IDbContextFactory<AppDbContext> dbFactory) : IPeriodReadModelWriter
{
    public async Task UpsertOnStartAsync(
        Guid periodId, Guid userId, DateOnly startDate, PeriodType type, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Periods.FindAsync([periodId], ct);
        if (existing is not null)
        {
            return;
        }
        db.Periods.Add(new PeriodEntity
        {
            PeriodId = periodId,
            UserId = userId,
            StartDate = startDate,
            EndDate = null,
            Status = "active",
            TotalIncome = 0m,
            AllocationEssentials = 0m,
            AllocationFun = 0m,
            AllocationDeposit = 0m,
            SavingsActual = null
        });
        _ = type;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAllocationAsync(
        Guid periodId, decimal totalIncome, decimal allocationEssentials,
        decimal allocationFun, decimal allocationDeposit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Periods
            .Where(p => p.PeriodId == periodId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.TotalIncome, totalIncome)
                .SetProperty(p => p.AllocationEssentials, allocationEssentials)
                .SetProperty(p => p.AllocationFun, allocationFun)
                .SetProperty(p => p.AllocationDeposit, allocationDeposit), ct);
    }

    public async Task UpdateSavingsActualAsync(Guid periodId, decimal savingsActual, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Periods
            .Where(p => p.PeriodId == periodId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SavingsActual, savingsActual), ct);
    }

    public async Task CloseAsync(Guid periodId, DateOnly endDate, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Periods
            .Where(p => p.PeriodId == periodId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.EndDate, (DateOnly?)endDate)
                .SetProperty(p => p.Status, "closed"), ct);
    }
}
