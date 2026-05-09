using System.Globalization;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.UserPlannedExpenses.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.Planned;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>Stage 14: команды /plan add/list/remove.</summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage14()
    {
        Receive<PlanReplyResult>(OnPlanReplyResult);
    }

    partial void HandlePlan(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserPlannedShardMarker>(out var shard))
        {
            _log.Error("UserPlanned shard region not registered.");
            return;
        }

        var trimmed = args.Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.PlanUsage()));
            return;
        }

        var verb = parts[0].ToLowerInvariant();
        var rest = parts.Length == 2 ? parts[1] : string.Empty;
        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);

        switch (verb)
        {
            case "add":
                HandlePlanAdd(update, userId, shard, rest);
                break;
            case "list":
                DispatchPlan(shard, update, userId, new ListPlanned(userId));
                break;
            case "remove":
                if (!Guid.TryParse(rest.Trim(), out var pid))
                {
                    Self.Tell(new OutgoingTelegramReply(update.ChatId, "Формат: `/plan remove <id>`."));
                    return;
                }
                DispatchPlan(shard, update, userId, new RemovePlanned(userId, pid));
                break;
            default:
                Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.PlanUsage()));
                break;
        }
    }

    private void HandlePlanAdd(IncomingTelegramUpdate update, Guid userId, IActorRef shard, string rest)
    {
        // /plan add <amount> <YYYY-MM-DD> <description>
        var tokens = rest.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 3)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId,
                "Формат: `/plan add <amount> <YYYY-MM-DD> <description>`."));
            return;
        }
        if (!AmountTextParser.TryParseAmount(tokens[0], out var amount))
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, $"Не разобрал сумму '{tokens[0]}'."));
            return;
        }
        if (!DateOnly.TryParseExact(tokens[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, $"Не разобрал дату '{tokens[1]}'. Формат: YYYY-MM-DD."));
            return;
        }
        var description = tokens[2].Trim();

        DispatchPlan(shard, update, userId, new AddPlanned(userId, amount, date, description));
    }

    private void DispatchPlan(IActorRef shard, IncomingTelegramUpdate update, Guid userId, object command)
    {
        var envelope = new ShardEnvelope(userId.ToString("N"), command);
        var self = Self;
        shard.Ask<object>(envelope, AskTimeout)
            .ContinueWith(t => (PlanReplyResult)new PlanReplyResult(update,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnPlanReplyResult(PlanReplyResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Plan command failed.");
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка."));
            return;
        }

        var text = msg.Reply switch
        {
            PlannedAdded a => $"Запланировано: {Format(a.Plan.Amount)} ₽ на {a.Plan.Date:yyyy-MM-dd} ({a.Plan.Description}). ID: {a.Plan.PlannedId}.",
            PlannedRemoved r => $"План {r.PlannedId} удалён.",
            PlannedConfirmed c => $"План {c.PlannedId} подтверждён.",
            PlannedRejected rj => $"Не удалось: {rj.Reason}",
            PlannedList list => FormatPlannedList(list),
            _ => "Не понял ответа."
        };
        Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, text));
    }

    private static string FormatPlannedList(PlannedList list)
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

    private sealed record PlanReplyResult(IncomingTelegramUpdate Update, object? Reply, AggregateException? Exception);
}
