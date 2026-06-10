using System.Globalization;
using System.Text;

namespace FinanceBot.Application.Actors.Reports;

/// <summary>Трата по категории за период (вход для сводки /stats).</summary>
public sealed record CategorySpend(string Category, decimal Amount);

/// <summary>
/// Чистое форматирование сводки /stats: топ категорий по сумме с долей в процентах.
/// Данные загружает <see cref="IReportBuilder"/> в Infrastructure; здесь — только текст.
/// </summary>
public static class StatsTextBuilder
{
    public const int DefaultTop = 5;

    public static string Build(
        IReadOnlyList<CategorySpend> rows,
        DateOnly periodStart,
        int periodsAgo,
        int top = DefaultTop)
    {
        var prefix = periodsAgo == 0
            ? "текущий период"
            : $"период {periodsAgo} назад";

        if (rows.Count == 0)
        {
            return $"📈 Трат за период нет ({prefix} с {periodStart:yyyy-MM-dd}).";
        }

        var total = rows.Sum(r => r.Amount);
        var sb = new StringBuilder(256);
        sb.AppendLine($"📈 Топ категорий за {prefix} с {periodStart:yyyy-MM-dd}:");
        foreach (var row in rows.OrderByDescending(r => r.Amount).Take(top))
        {
            var pct = total > 0m
                ? Math.Round(row.Amount / total * 100m, MidpointRounding.AwayFromZero)
                : 0m;
            sb.AppendLine($"• {row.Category} — {Format(row.Amount)} ₽ ({pct}%)");
        }
        sb.Append($"Всего: {Format(total)} ₽");
        return sb.ToString();
    }

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
