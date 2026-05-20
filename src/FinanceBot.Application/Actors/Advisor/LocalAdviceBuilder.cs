using System.Globalization;
using System.Text;
using FinanceBot.Domain.Events.Advisor;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Advisor;

/// <summary>
/// Локальные эвристики для AdvisorActor (fallback при недоступности Claude).
/// Возвращает связный текст до ~1200 символов: сравнение с предыдущим периодом по категориям,
/// прогноз перерасхода по бакетам, осталось дней до конца периода.
/// </summary>
public static class LocalAdviceBuilder
{
    public static string Build(AdvisorSnapshot snap, AdvisorTickType tickType)
    {
        var sb = new StringBuilder(512);
        var scope = tickType switch
        {
            AdvisorTickType.Weekly => "еженедельный обзор",
            AdvisorTickType.Monthly => "ежемесячный обзор",
            _ => "локальный совет"
        };
        sb.AppendLine($"📊 {scope} (Claude недоступен — рассчитал локально).");

        if (snap.CurrentPeriod is not { } cur)
        {
            sb.AppendLine("Активного бюджетного периода нет. Запиши доход через /income.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine();
        sb.AppendLine(FormatPeriodLine(cur, snap.DaysToEndOfPeriod));
        AppendBucketStatus(sb, cur, snap.DaysToEndOfPeriod);

        AppendCategoryComparison(sb, snap);
        AppendTopExpenses(sb, snap);

        var result = sb.ToString().TrimEnd();
        return result.Length <= 1500 ? result : result[..1500];
    }

    private static string FormatPeriodLine(PeriodSnapshot p, int? daysToEnd)
    {
        var income = p.TotalIncome.ToString("0.00", CultureInfo.InvariantCulture);
        var spent = (p.SpentEssentials + p.SpentFun + p.SpentDeposit).ToString("0.00", CultureInfo.InvariantCulture);
        var days = daysToEnd is { } d ? $", дней до конца ≈ {d}" : string.Empty;
        return $"Период с {p.StartDate:yyyy-MM-dd}: доход {income}, потрачено {spent}{days}.";
    }

    private static void AppendBucketStatus(StringBuilder sb, PeriodSnapshot p, int? daysToEnd)
    {
        AppendBucket(sb, "Essentials", p.SpentEssentials, p.AllocationEssentials, daysToEnd);
        AppendBucket(sb, "Fun", p.SpentFun, p.AllocationFun, daysToEnd);
        AppendBucket(sb, "Deposit", p.SpentDeposit, p.AllocationDeposit, daysToEnd);
    }

    private static void AppendBucket(StringBuilder sb, string name, decimal spent, decimal allocation, int? daysToEnd)
    {
        if (allocation <= 0m)
        {
            sb.AppendLine($"• {name}: аллокация не задана.");
            return;
        }
        var remaining = allocation - spent;
        var percent = Math.Round(spent / allocation * 100m, MidpointRounding.AwayFromZero);
        var line = $"• {name}: {spent:0.00} / {allocation:0.00} ({percent}%), осталось {remaining:0.00}";
        if (remaining < 0m)
        {
            line += " — перерасход.";
        }
        else if (daysToEnd is { } d and > 0 && remaining > 0m)
        {
            var perDay = remaining / d;
            line += $". На день остаётся ≈ {perDay:0.00}.";
        }
        else
        {
            line += '.';
        }
        sb.AppendLine(line);
    }

    private static void AppendCategoryComparison(StringBuilder sb, AdvisorSnapshot snap)
    {
        if (snap.PreviousByCategory.Count == 0 || snap.CurrentByCategory.Count == 0)
        {
            return;
        }
        var previousMap = snap.PreviousByCategory.ToDictionary(c => c.Category, c => c.Spent);

        var deltas = new List<(Category category, decimal current, decimal previous, decimal percent)>();
        foreach (var c in snap.CurrentByCategory)
        {
            if (!previousMap.TryGetValue(c.Category, out var prev) || prev <= 0m)
            {
                continue;
            }
            var pct = Math.Round((c.Spent - prev) / prev * 100m, MidpointRounding.AwayFromZero);
            if (Math.Abs(pct) >= 25m)
            {
                deltas.Add((c.Category, c.Spent, prev, pct));
            }
        }
        if (deltas.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Изменения по категориям vs предыдущий период:");
        foreach (var d in deltas.OrderByDescending(x => Math.Abs(x.percent)).Take(5))
        {
            var sign = d.percent > 0 ? "+" : string.Empty;
            sb.AppendLine($"• {d.category}: {d.current:0.00} ({sign}{d.percent}% к {d.previous:0.00}).");
        }
    }

    private static void AppendTopExpenses(StringBuilder sb, AdvisorSnapshot snap)
    {
        if (snap.TopExpenses.Count == 0)
        {
            return;
        }
        sb.AppendLine();
        sb.AppendLine("Крупнейшие траты периода:");
        foreach (var e in snap.TopExpenses.Take(3))
        {
            sb.AppendLine($"• {e.OccurredAt:yyyy-MM-dd} {e.Description}: {e.Amount:0.00} [{e.Category}].");
        }
    }
}
