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
        var p = await db.Periods.FindAsync([periodId], ct);
        if (p is null)
        {
            return;
        }
        p.TotalIncome = totalIncome;
        p.AllocationEssentials = allocationEssentials;
        p.AllocationFun = allocationFun;
        p.AllocationDeposit = allocationDeposit;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateSavingsActualAsync(Guid periodId, decimal savingsActual, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Periods.FindAsync([periodId], ct);
        if (p is null)
        {
            return;
        }
        p.SavingsActual = savingsActual;
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(Guid periodId, DateOnly endDate, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Periods.FindAsync([periodId], ct);
        if (p is null)
        {
            return;
        }
        p.EndDate = endDate;
        p.Status = "closed";
        await db.SaveChangesAsync(ct);
    }
}
