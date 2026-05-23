using System.Globalization;
using System.Text;
using Akka.Actor;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.UserPlannedExpenses;
using FinanceBot.Application.Actors.UserPlannedExpenses.Messages;
using FinanceBot.Domain.Commands.Planned;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class PlanHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Plan;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserPlannedShardMarker>();
        if (shard is null)
        {
            return;
        }

        var trimmed = ctx.ArgumentLine.Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            ctx.Reply(TelegramReplies.PlanUsage());
            return;
        }

        var verb = parts[0].ToLowerInvariant();
        var rest = parts.Length == 2 ? parts[1] : string.Empty;
        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);

        switch (verb)
        {
            case "add":
                HandleAdd(ctx, userId, shard, rest);
                break;
            case "list":
                Dispatch(ctx, shard, userId, new ListPlanned(userId));
                break;
            case "remove":
                if (!Guid.TryParse(rest.Trim(), out var pid))
                {
                    ctx.Reply("Формат: `/plan remove <id>`.");
                    return;
                }
                Dispatch(ctx, shard, userId, new RemovePlanned(userId, pid));
                break;
            default:
                ctx.Reply(TelegramReplies.PlanUsage());
                break;
        }
    }

    private static void HandleAdd(TelegramCommandContext ctx, Guid userId, IActorRef shard, string rest)
    {
        var tokens = rest.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 3)
        {
            ctx.Reply("Формат: `/plan add <amount> <YYYY-MM-DD> <description>`.");
            return;
        }
        if (!AmountTextParser.TryParseAmount(tokens[0], out var amount))
        {
            ctx.Reply($"Не разобрал сумму '{tokens[0]}'.");
            return;
        }
        if (!DateOnly.TryParseExact(tokens[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            ctx.Reply($"Не разобрал дату '{tokens[1]}'. Формат: YYYY-MM-DD.");
            return;
        }
        var description = tokens[2].Trim();

        Dispatch(ctx, shard, userId, new AddPlanned(userId, amount, date, description));
    }

    private static void Dispatch(TelegramCommandContext ctx, IActorRef shard, Guid userId, object command)
        => ctx.AskShardAndReplyText(shard, userId, command, reply => reply switch
        {
            PlannedAdded a => $"Запланировано: {Format(a.Plan.Amount)} ₽ на {a.Plan.Date:yyyy-MM-dd} ({a.Plan.Description}). ID: {a.Plan.PlannedId}.",
            PlannedRemoved r => $"План {r.PlannedId} удалён.",
            PlannedConfirmed c => $"План {c.PlannedId} подтверждён.",
            PlannedRejected rj => $"Не удалось: {rj.Reason}",
            PlannedList list => FormatList(list),
            _ => "Не понял ответа."
        }, "Plan");

    private static string FormatList(PlannedList list)
    {
        if (list.Plans.Count == 0)
        {
            return "Запланированных трат нет.";
        }
        var sb = new StringBuilder();
        sb.AppendLine("Запланированные траты:");
        foreach (var p in list.Plans)
        {
            sb.Append("- ").Append(p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
              .Append(' ').Append(Format(p.Amount)).Append(" ₽ — ").Append(p.Description)
              .Append(" (id: ").Append(p.PlannedId).AppendLine(")");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Format(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}
