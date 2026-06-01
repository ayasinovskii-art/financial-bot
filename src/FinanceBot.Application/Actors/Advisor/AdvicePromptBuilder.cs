using System.Globalization;
using System.Text;
using FinanceBot.Application.Actors.User;
using FinanceBot.Domain.Events.Advisor;

namespace FinanceBot.Application.Actors.Advisor;

/// <summary>
/// Сборщик user-промпта для запроса в Claude (use case = Advice). System-prompt задаётся в ClaudePrompts.
/// </summary>
public static class AdvicePromptBuilder
{
    public static string Build(
        AdvisorSnapshot snap,
        AdvisorTickType tickType,
        string? userQuestion = null,
        IReadOnlyList<AdviceConversationTurn>? conversation = null)
    {
        var sb = new StringBuilder(1024);
        var scope = tickType switch
        {
            AdvisorTickType.Weekly => "еженедельный обзор",
            AdvisorTickType.Monthly => "ежемесячный обзор",
            _ => "разовый запрос"
        };
        sb.AppendLine($"Контекст: {scope}.");

        if (snap.Settings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Настройки пользователя:");
            foreach (var kv in snap.Settings.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"- {kv.Key} = {kv.Value}");
            }
        }

        if (snap.CurrentPeriod is { } cur)
        {
            sb.AppendLine($"Текущий период: с {cur.StartDate:yyyy-MM-dd}, доход {Fmt(cur.TotalIncome)}.");
            sb.AppendLine($"Аллокации (essentials/fun/deposit): {Fmt(cur.AllocationEssentials)} / {Fmt(cur.AllocationFun)} / {Fmt(cur.AllocationDeposit)}.");
            sb.AppendLine($"Потрачено (essentials/fun/deposit): {Fmt(cur.SpentEssentials)} / {Fmt(cur.SpentFun)} / {Fmt(cur.SpentDeposit)}.");
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
                sb.AppendLine($"- {c.Category} ({c.Bucket}): {Fmt(c.Spent)}, операций {c.Count}.");
            }
        }

        if (snap.PreviousPeriod is { } prev)
        {
            sb.AppendLine();
            sb.AppendLine($"Предыдущий период с {prev.StartDate:yyyy-MM-dd}: доход {Fmt(prev.TotalIncome)}, накопления {Fmt(prev.SavingsActual ?? 0m)}.");
            if (snap.PreviousByCategory.Count > 0)
            {
                sb.AppendLine("Расходы по категориям (предыдущий период):");
                foreach (var c in snap.PreviousByCategory.OrderByDescending(c => c.Spent).Take(8))
                {
                    sb.AppendLine($"- {c.Category}: {Fmt(c.Spent)}.");
                }
            }
        }

        if (snap.TopExpenses.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Крупнейшие траты:");
            foreach (var e in snap.TopExpenses.Take(5))
            {
                sb.AppendLine($"- {e.OccurredAt:yyyy-MM-dd} {e.Description}: {Fmt(e.Amount)} [{e.Category}].");
            }
        }

        if (conversation is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Предыдущий диалог (для контекста встречных вопросов):");
            foreach (var turn in conversation)
            {
                sb.AppendLine($"Пользователь: {turn.Question.Trim()}");
                sb.AppendLine($"Ты: {turn.Answer.Trim()}");
            }
        }

        if (!string.IsNullOrWhiteSpace(userQuestion))
        {
            sb.AppendLine();
            sb.AppendLine("Вопрос пользователя:");
            sb.AppendLine(userQuestion.Trim());
            sb.AppendLine();
            sb.AppendLine("Ответь именно на этот вопрос, опираясь на данные выше. Совет до 1500 символов, без markdown-таблиц, одним связным сообщением.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("Дай совет до 1500 символов, без markdown-таблиц, одним связным сообщением.");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Fmt(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}
