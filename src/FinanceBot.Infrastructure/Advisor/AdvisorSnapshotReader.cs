using FinanceBot.Application.Actors.Advisor;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Advisor;

/// <summary>
/// Реализация <see cref="IAdvisorSnapshotReader"/> поверх AppDbContext (read-only).
/// Тянет текущий + предыдущий период, агрегирует расходы по категориям, топ-5 крупных трат
/// и считает days-to-end (по start_date следующего ожидаемого salary cycle, fallback 30 дней).
/// </summary>
public sealed class AdvisorSnapshotReader(
    IDbContextFactory<AppDbContext> dbFactory,
    ICategoryBucketMap bucketMap) : IAdvisorSnapshotReader
{
    public async Task<AdvisorSnapshot> BuildAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var periods = await db.Periods
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.StartDate)
            .Take(2)
            .ToListAsync(ct).ConfigureAwait(false);

        PeriodEntity? current = periods.FirstOrDefault(p => p.Status == "active") ?? periods.FirstOrDefault();
        PeriodEntity? previous = periods.FirstOrDefault(p => current is null || p.PeriodId != current.PeriodId);

        var currentSnap = current is null
            ? null
            : await BuildPeriodSnapshotAsync(db, current, ct).ConfigureAwait(false);
        var previousSnap = previous is null
            ? null
            : await BuildPeriodSnapshotAsync(db, previous, ct).ConfigureAwait(false);

        var currentByCategory = current is null
            ? Array.Empty<CategorySnapshot>()
            : await GroupByCategoryAsync(db, userId, current.PeriodId, ct).ConfigureAwait(false);
        var previousByCategory = previous is null
            ? Array.Empty<CategorySnapshot>()
            : await GroupByCategoryAsync(db, userId, previous.PeriodId, ct).ConfigureAwait(false);

        var topExpenses = current is null
            ? Array.Empty<TopExpense>()
            : await TopExpensesAsync(db, userId, current.PeriodId, ct).ConfigureAwait(false);

        int? daysToEnd = ComputeDaysToEndOfPeriod(currentSnap, now);

        return new AdvisorSnapshot(
            UserId: userId,
            BuiltAt: now,
            CurrentPeriod: currentSnap,
            PreviousPeriod: previousSnap,
            CurrentByCategory: currentByCategory,
            PreviousByCategory: previousByCategory,
            TopExpenses: topExpenses,
            DaysToEndOfPeriod: daysToEnd);
    }

    private async Task<PeriodSnapshot> BuildPeriodSnapshotAsync(AppDbContext db, PeriodEntity p, CancellationToken ct)
    {
        var totals = await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == p.UserId && e.PeriodId == p.PeriodId)
            .GroupBy(e => e.Bucket)
            .Select(g => new { Bucket = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct).ConfigureAwait(false);

        decimal spentE = 0m, spentF = 0m, spentD = 0m;
        foreach (var row in totals)
        {
            if (string.Equals(row.Bucket, nameof(Bucket.Essentials), StringComparison.OrdinalIgnoreCase)) spentE = row.Total;
            else if (string.Equals(row.Bucket, nameof(Bucket.Fun), StringComparison.OrdinalIgnoreCase)) spentF = row.Total;
            else if (string.Equals(row.Bucket, nameof(Bucket.Deposit), StringComparison.OrdinalIgnoreCase)) spentD = row.Total;
        }

        return new PeriodSnapshot(
            PeriodId: p.PeriodId,
            StartDate: p.StartDate,
            EndDate: p.EndDate,
            Status: p.Status,
            TotalIncome: p.TotalIncome,
            AllocationEssentials: p.AllocationEssentials,
            AllocationFun: p.AllocationFun,
            AllocationDeposit: p.AllocationDeposit,
            SpentEssentials: spentE,
            SpentFun: spentF,
            SpentDeposit: spentD,
            SavingsActual: p.SavingsActual);
    }

    private async Task<IReadOnlyList<CategorySnapshot>> GroupByCategoryAsync(
        AppDbContext db, Guid userId, Guid periodId, CancellationToken ct)
    {
        var rows = await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.PeriodId == periodId)
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Spent = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        var result = new List<CategorySnapshot>(rows.Count);
        foreach (var r in rows)
        {
            if (!CategoryExtensions.TryParse(r.Category, out var category))
            {
                category = Category.Other;
            }
            result.Add(new CategorySnapshot(category, bucketMap.Map(category), r.Spent, r.Count));
        }
        return result;
    }

    private static async Task<IReadOnlyList<TopExpense>> TopExpensesAsync(
        AppDbContext db, Guid userId, Guid periodId, CancellationToken ct)
    {
        var rows = await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.PeriodId == periodId)
            .OrderByDescending(e => e.Amount)
            .Take(5)
            .Select(e => new { e.ExpenseId, e.OccurredAt, e.Amount, e.Category, e.Description })
            .ToListAsync(ct).ConfigureAwait(false);

        var result = new List<TopExpense>(rows.Count);
        foreach (var r in rows)
        {
            if (!CategoryExtensions.TryParse(r.Category, out var category))
            {
                category = Category.Other;
            }
            result.Add(new TopExpense(r.ExpenseId, r.OccurredAt, r.Amount, category, r.Description));
        }
        return result;
    }

    private static int? ComputeDaysToEndOfPeriod(PeriodSnapshot? current, DateTimeOffset now)
    {
        if (current is null || current.Status != "active")
        {
            return null;
        }
        // Период открывается на /income — длина обычно 1 месяц от StartDate.
        var assumedEnd = current.StartDate.AddMonths(1);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var days = assumedEnd.DayNumber - today.DayNumber;
        return days < 0 ? 0 : days;
    }
}
