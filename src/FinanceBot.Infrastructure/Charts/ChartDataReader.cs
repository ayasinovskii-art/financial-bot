using FinanceBot.Application.Actors.Charts;
using FinanceBot.Domain.Events.Reports;
using FinanceBot.Domain.ValueObjects;
using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Charts;

/// <summary>
/// Реализация <see cref="IChartDataReader"/> поверх AppDbContext.
/// Читает данные для рендерера из read-model (app.expenses / app.periods).
/// </summary>
public sealed class ChartDataReader(IDbContextFactory<AppDbContext> dbFactory) : IChartDataReader
{
    public async Task<ChartDataSet?> LoadAsync(Guid userId, ChartType type, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var active = await db.Periods
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == "active")
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return type switch
        {
            ChartType.CategoryPie => await LoadCategoryPieAsync(db, userId, active?.PeriodId, ct).ConfigureAwait(false),
            ChartType.DailyBar => await LoadDailyBarAsync(db, userId, DateOnly.FromDateTime(DateTime.UtcNow), ct).ConfigureAwait(false),
            ChartType.BucketUtilization => await LoadBucketUtilizationAsync(db, userId, active?.PeriodId, ct).ConfigureAwait(false),
            ChartType.SavingsLine => await LoadSavingsLineAsync(db, userId, ct).ConfigureAwait(false),
            _ => null
        };
    }

    private static async Task<ChartDataSet?> LoadCategoryPieAsync(AppDbContext db, Guid userId, Guid? periodId, CancellationToken ct)
    {
        if (periodId is null) return new CategoryPieData { Title = "Категории (нет активного периода)", Slices = Array.Empty<CategorySlice>() };

        var rows = await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.PeriodId == periodId.Value)
            .GroupBy(e => e.Category)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct).ConfigureAwait(false);

        var slices = rows
            .Select(r =>
            {
                if (!CategoryExtensions.TryParse(r.Key, out var c)) c = Category.Other;
                return new CategorySlice(c, r.Sum);
            })
            .ToArray();
        return new CategoryPieData { Title = "Расходы по категориям", Slices = slices };
    }

    private static async Task<ChartDataSet> LoadDailyBarAsync(AppDbContext db, Guid userId, DateOnly today, CancellationToken ct)
    {
        var fromDate = today.AddDays(-29);
        var fromUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rawRows = await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAt >= fromUtc)
            .Select(e => new { e.OccurredAt, e.Amount })
            .ToListAsync(ct).ConfigureAwait(false);

        var grouped = rawRows
            .GroupBy(r => DateOnly.FromDateTime(r.OccurredAt.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var days = new List<DailyBucket>(30);
        for (var i = 0; i < 30; i++)
        {
            var d = fromDate.AddDays(i);
            days.Add(new DailyBucket(d, grouped.TryGetValue(d, out var v) ? v : 0m));
        }
        return new DailyBarData { Title = "Траты по дням (30 дней)", Days = days };
    }

    private static async Task<ChartDataSet?> LoadBucketUtilizationAsync(AppDbContext db, Guid userId, Guid? periodId, CancellationToken ct)
    {
        if (periodId is null) return null;
        var period = await db.Periods.AsNoTracking().FirstOrDefaultAsync(p => p.PeriodId == periodId.Value, ct).ConfigureAwait(false);
        if (period is null) return null;

        var totalsByBucket = await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.PeriodId == periodId.Value)
            .GroupBy(e => e.Bucket)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct).ConfigureAwait(false);

        decimal e = 0m, f = 0m, d = 0m;
        foreach (var row in totalsByBucket)
        {
            if (string.Equals(row.Key, nameof(Bucket.Essentials), StringComparison.OrdinalIgnoreCase)) e = row.Sum;
            else if (string.Equals(row.Key, nameof(Bucket.Fun), StringComparison.OrdinalIgnoreCase)) f = row.Sum;
            else if (string.Equals(row.Key, nameof(Bucket.Deposit), StringComparison.OrdinalIgnoreCase)) d = row.Sum;
        }
        return new BucketUtilizationData
        {
            Title = "Утилизация бакетов",
            SpentEssentials = e, AllocationEssentials = period.AllocationEssentials,
            SpentFun = f, AllocationFun = period.AllocationFun,
            SpentDeposit = d, AllocationDeposit = period.AllocationDeposit
        };
    }

    private static async Task<ChartDataSet> LoadSavingsLineAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var rows = await db.Periods
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.StartDate)
            .Select(p => new { p.StartDate, p.SavingsActual })
            .ToListAsync(ct).ConfigureAwait(false);

        var points = rows
            .Where(r => r.SavingsActual.HasValue)
            .Select(r => new SavingsPoint(r.StartDate, r.SavingsActual!.Value))
            .ToArray();

        return new SavingsLineData { Title = "Накопления по периодам", Points = points };
    }
}
