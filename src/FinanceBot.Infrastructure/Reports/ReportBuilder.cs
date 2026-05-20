using System.Globalization;
using System.Text;
using FinanceBot.Application.Actors.Reports;
using FinanceBot.Domain.ValueObjects;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Reports;

/// <summary>
/// Сборка текстового отчёта за период. Тянет данные из app.periods/app.expenses/app.incomes.
/// </summary>
public sealed class ReportBuilder(IDbContextFactory<AppDbContext> dbFactory) : IReportBuilder
{
    public async Task<ReportResult> BuildAsync(Guid userId, int periodsAgo, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var ordered = await db.Periods
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.StartDate)
            .Take(periodsAgo + 2)
            .ToListAsync(ct).ConfigureAwait(false);

        if (ordered.Count == 0 || periodsAgo >= ordered.Count)
        {
            return new ReportResult(HasData: false, Text: "Нет данных за указанный период.");
        }

        var period = ordered[periodsAgo];
        var previous = periodsAgo + 1 < ordered.Count ? ordered[periodsAgo + 1] : null;

        var current = await LoadAggregateAsync(db, userId, period, ct).ConfigureAwait(false);
        var prev = previous is null ? null : await LoadAggregateAsync(db, userId, previous, ct).ConfigureAwait(false);
        var topExpenses = await LoadTopExpensesAsync(db, userId, period.PeriodId, ct).ConfigureAwait(false);

        var sb = new StringBuilder(1024);
        sb.AppendLine(FormatHeader(period, periodsAgo));
        sb.AppendLine();
        sb.AppendLine($"Доход: {Money(period.TotalIncome)}");
        sb.AppendLine($"Расходы: {Money(current.TotalSpent)}");
        sb.AppendLine($"Накопления (факт): {(period.SavingsActual is { } sv ? Money(sv) : "—")}");

        sb.AppendLine();
        sb.AppendLine("По бакетам:");
        AppendBucketLine(sb, "Essentials", current.SpentEssentials, period.AllocationEssentials);
        AppendBucketLine(sb, "Fun", current.SpentFun, period.AllocationFun);
        AppendBucketLine(sb, "Deposit", current.SpentDeposit, period.AllocationDeposit);

        if (current.ByCategory.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("По категориям:");
            foreach (var c in current.ByCategory.OrderByDescending(c => c.Spent))
            {
                sb.AppendLine($"• {c.Category}: {Money(c.Spent)}");
            }
        }

        if (topExpenses.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Крупнейшие траты:");
            foreach (var e in topExpenses)
            {
                sb.AppendLine($"• {e.OccurredAt:yyyy-MM-dd} {e.Description}: {Money(e.Amount)} [{e.Category}]");
            }
        }

        if (prev is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Сравнение с предыдущим периодом (с {previous!.StartDate:yyyy-MM-dd}):");
            AppendDelta(sb, "Доход", period.TotalIncome, previous.TotalIncome);
            AppendDelta(sb, "Расходы", current.TotalSpent, prev.TotalSpent);
            AppendDelta(sb, "Essentials", current.SpentEssentials, prev.SpentEssentials);
            AppendDelta(sb, "Fun", current.SpentFun, prev.SpentFun);
            AppendDelta(sb, "Deposit", current.SpentDeposit, prev.SpentDeposit);
        }

        return new ReportResult(HasData: true, Text: sb.ToString().TrimEnd());
    }

    private static string FormatHeader(PeriodEntity period, int periodsAgo)
    {
        var status = period.Status == "active" ? "(активный)" : "(закрытый)";
        var endLabel = period.EndDate is { } end ? $" — {end:yyyy-MM-dd}" : string.Empty;
        var prefix = periodsAgo == 0 ? "Отчёт за текущий период" : $"Отчёт за период {periodsAgo} назад";
        return $"📊 {prefix} с {period.StartDate:yyyy-MM-dd}{endLabel} {status}.";
    }

    private static void AppendBucketLine(StringBuilder sb, string name, decimal spent, decimal allocation)
    {
        var remaining = allocation - spent;
        var pct = allocation > 0m ? Math.Round(spent / allocation * 100m, MidpointRounding.AwayFromZero) : 0m;
        sb.AppendLine($"• {name}: {Money(spent)} / {Money(allocation)} ({pct}%), осталось {Money(remaining)}");
    }

    private static void AppendDelta(StringBuilder sb, string label, decimal current, decimal previous)
    {
        if (previous == 0m)
        {
            sb.AppendLine($"• {label}: {Money(current)} (предыдущее = 0)");
            return;
        }
        var pct = Math.Round((current - previous) / previous * 100m, MidpointRounding.AwayFromZero);
        var sign = pct >= 0m ? "+" : string.Empty;
        sb.AppendLine($"• {label}: {Money(current)} ({sign}{pct}% к {Money(previous)})");
    }

    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static async Task<PeriodAggregate> LoadAggregateAsync(AppDbContext db, Guid userId, PeriodEntity p, CancellationToken ct)
    {
        var rows = await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.PeriodId == p.PeriodId)
            .Select(e => new { e.Amount, e.Bucket, e.Category })
            .ToListAsync(ct).ConfigureAwait(false);

        decimal eAmt = 0m, fAmt = 0m, dAmt = 0m;
        var byCategory = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            if (string.Equals(r.Bucket, nameof(Bucket.Essentials), StringComparison.OrdinalIgnoreCase)) eAmt += r.Amount;
            else if (string.Equals(r.Bucket, nameof(Bucket.Fun), StringComparison.OrdinalIgnoreCase)) fAmt += r.Amount;
            else if (string.Equals(r.Bucket, nameof(Bucket.Deposit), StringComparison.OrdinalIgnoreCase)) dAmt += r.Amount;
            byCategory[r.Category] = byCategory.GetValueOrDefault(r.Category) + r.Amount;
        }

        var categoryList = byCategory.Select(kv => new CategoryAggregate(kv.Key, kv.Value)).ToArray();
        return new PeriodAggregate(eAmt + fAmt + dAmt, eAmt, fAmt, dAmt, categoryList);
    }

    private static async Task<IReadOnlyList<TopExpenseRow>> LoadTopExpensesAsync(AppDbContext db, Guid userId, Guid periodId, CancellationToken ct)
    {
        var rows = await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.PeriodId == periodId)
            .OrderByDescending(e => e.Amount)
            .Take(5)
            .Select(e => new TopExpenseRow(e.ExpenseId, e.OccurredAt, e.Amount, e.Category, e.Description))
            .ToListAsync(ct).ConfigureAwait(false);
        return rows;
    }

    private sealed record PeriodAggregate(
        decimal TotalSpent,
        decimal SpentEssentials,
        decimal SpentFun,
        decimal SpentDeposit,
        IReadOnlyList<CategoryAggregate> ByCategory);

    private sealed record CategoryAggregate(string Category, decimal Spent);
    private sealed record TopExpenseRow(Guid ExpenseId, DateTimeOffset OccurredAt, decimal Amount, string Category, string Description);
}
