using System.Globalization;
using System.Text;
using FinanceBot.Domain.Events.Advisor;

namespace FinanceBot.Application.Actors.Advisor;

/// <summary>
/// Сборщик user-промпта для запроса в Claude (use case = Advice). System-prompt задаётся в ClaudePrompts.
/// </summary>
public static class AdvicePromptBuilder
{
    public static string Build(AdvisorSnapshot snap, AdvisorTickType tickType)
    {
        var sb = new StringBuilder(1024);
        var scope = tickType switch
        {
            AdvisorTickType.Weekly => "еженедельный обзор",
            AdvisorTickType.Monthly => "ежемесячный обзор",
            _ => "разовый запрос"
        };
        sb.AppendLine($"Контекст: {scope}.");

        if (snap.CurrentPeriod is { } cur)
        {
            sb.AppendLine($"Текущий период: с {cur.StartDate:yyyy-MM-dd}, доход {Money(cur.TotalIncome)}.");
            sb.AppendLine($"Аллокации (essentials/fun/deposit): {Money(cur.AllocationEssentials)} / {Money(cur.AllocationFun)} / {Money(cur.AllocationDeposit)}.");
            sb.AppendLine($"Потрачено (essentials/fun/deposit): {Money(cur.SpentEssentials)} / {Money(cur.SpentFun)} / {Money(cur.SpentDeposit)}.");
            if (snap.DaysToEndOfPeriod is { } d)
            {
                sb.AppendLine($"Дней до конца периода: ~{d}.");
            }
        }
        else
        {
            sb.AppendLine("Активного периода нет.");
        }

        if (snap.CurrentByCategory.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Расходы по категориям (текущий период):");
            foreach (var c in snap.CurrentByCategory.OrderByDescending(c => c.Spent).Take(8))
            {
                sb.AppendLine($"- {c.Category} ({c.Bucket}): {Money(c.Spent)}, операций {c.Count}.");
            }
        }

        if (snap.PreviousPeriod is { } prev)
        {
            sb.AppendLine();
            sb.AppendLine($"Предыдущий период с {prev.StartDate:yyyy-MM-dd}: доход {Money(prev.TotalIncome)}, накопления {Money(prev.SavingsActual ?? 0m)}.");
            if (snap.PreviousByCategory.Count > 0)
            {
                sb.AppendLine("Расходы по категориям (предыдущий период):");
                foreach (var c in snap.PreviousByCategory.OrderByDescending(c => c.Spent).Take(8))
                {
                    sb.AppendLine($"- {c.Category}: {Money(c.Spent)}.");
                }
            }
        }

        if (snap.TopExpenses.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Крупнейшие траты:");
            foreach (var e in snap.TopExpenses.Take(5))
            {
                sb.AppendLine($"- {e.OccurredAt:yyyy-MM-dd} {e.Description}: {Money(e.Amount)} [{e.Category}].");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Дай совет до 1500 символов, без markdown-таблиц, одним связным сообщением.");
        return sb.ToString().TrimEnd();
    }

    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}
