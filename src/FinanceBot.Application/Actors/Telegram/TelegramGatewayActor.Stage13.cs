using System.Globalization;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.UserTemplates;
using FinanceBot.Application.Actors.UserTemplates.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.Templates;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>Stage 13: команды /template add/list/remove.</summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage13()
    {
        Receive<TemplateReplyResult>(OnTemplateReplyResult);
    }

    partial void HandleTemplate(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserTemplatesShardMarker>(out var shard))
        {
            _log.Error("UserTemplates shard region not registered.");
            return;
        }

        var trimmed = args.Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.TemplateUsage()));
            return;
        }

        var verb = parts[0].ToLowerInvariant();
        var rest = parts.Length == 2 ? parts[1] : string.Empty;
        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);

        switch (verb)
        {
            case "add":
                HandleTemplateAdd(update, userId, shard, rest);
                break;
            case "list":
                DispatchTemplate(shard, update, userId, new ListTemplates(userId));
                break;
            case "remove":
                if (string.IsNullOrWhiteSpace(rest))
                {
                    Self.Tell(new OutgoingTelegramReply(update.ChatId, "Формат: `/template remove <name>`."));
                    return;
                }
                DispatchTemplate(shard, update, userId, new RemoveTemplate(userId, rest.Trim()));
                break;
            default:
                Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.TemplateUsage()));
                break;
        }
    }

    private void HandleTemplateAdd(IncomingTelegramUpdate update, Guid userId, IActorRef shard, string rest)
    {
        // /template add <name> <amount> <schedule> [<category>]
        var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 3)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId,
                "Формат: `/template add <name> <amount> <schedule> [<category>]`. Например: `/template add lunch 700 weekdays DiningOut`."));
            return;
        }

        var name = tokens[0];
        if (!AmountTextParser.TryParseAmount(tokens[1], out var amount))
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, $"Не разобрал сумму '{tokens[1]}'."));
            return;
        }
        if (!ScheduleSpecParser.TryParse(tokens[2], out var schedule, out var scheduleError))
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, scheduleError));
            return;
        }

        Category? category = null;
        if (tokens.Length >= 4)
        {
            if (!CategoryExtensions.TryParse(tokens[3], out var cat))
            {
                Self.Tell(new OutgoingTelegramReply(update.ChatId, $"Неизвестная категория '{tokens[3]}'."));
                return;
            }
            category = cat;
        }

        DispatchTemplate(shard, update, userId, new AddTemplate(userId, name, amount, schedule!, category));
    }

    private void DispatchTemplate(IActorRef shard, IncomingTelegramUpdate update, Guid userId, object command)
    {
        var envelope = new ShardEnvelope(userId.ToString("N"), command);
        var self = Self;
        shard.Ask<object>(envelope, AskTimeout)
            .ContinueWith(t => (TemplateReplyResult)new TemplateReplyResult(update,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnTemplateReplyResult(TemplateReplyResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Template command failed.");
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка."));
            return;
        }

        var text = msg.Reply switch
        {
            TemplateAdded a => $"Шаблон '{a.Template.Name}' добавлен ({Format(a.Template.Amount)} ₽, {a.Template.Schedule.Format()}{(a.Template.Category is { } c ? ", " + c : string.Empty)}).",
            TemplateRemoved r => $"Шаблон '{r.Name}' удалён.",
            TemplateRejected rj => $"Не удалось: {rj.Reason}",
            TemplateList list => FormatTemplateList(list),
            _ => "Не понял ответа."
        };
        Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, text));
    }

    private static string FormatTemplateList(TemplateList list)
    {
        if (list.Templates.Count == 0)
        {
            return "Шаблонов нет. Добавь через `/template add`.";
        }
        var sb = new StringBuilder();
        sb.AppendLine("Шаблоны:");
        foreach (var t in list.Templates)
        {
            sb.Append("- ").Append(t.Name).Append(' ')
              .Append(Format(t.Amount)).Append(" ₽ — ")
              .Append(t.Schedule.Format());
            if (t.Category is { } cat)
            {
                sb.Append(" (").Append(cat).Append(')');
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private sealed record TemplateReplyResult(IncomingTelegramUpdate Update, object? Reply, AggregateException? Exception);
}
