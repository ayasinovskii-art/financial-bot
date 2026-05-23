using System.Globalization;
using System.Text;
using Akka.Actor;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.UserTemplates;
using FinanceBot.Application.Actors.UserTemplates.Messages;
using FinanceBot.Domain.Commands.Templates;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class TemplateHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Template;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserTemplatesShardMarker>();
        if (shard is null)
        {
            return;
        }

        var trimmed = ctx.ArgumentLine.Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            ctx.Reply(TelegramReplies.TemplateUsage());
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
                Dispatch(ctx, shard, userId, new ListTemplates(userId));
                break;
            case "remove":
                if (string.IsNullOrWhiteSpace(rest))
                {
                    ctx.Reply("Формат: `/template remove <name>`.");
                    return;
                }
                Dispatch(ctx, shard, userId, new RemoveTemplate(userId, rest.Trim()));
                break;
            default:
                ctx.Reply(TelegramReplies.TemplateUsage());
                break;
        }
    }

    private static void HandleAdd(TelegramCommandContext ctx, Guid userId, IActorRef shard, string rest)
    {
        var tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 3)
        {
            ctx.Reply("Формат: `/template add <name> <amount> <schedule> [<category>]`. Например: `/template add lunch 700 weekdays DiningOut`.");
            return;
        }

        var name = tokens[0];
        if (!AmountTextParser.TryParseAmount(tokens[1], out var amount))
        {
            ctx.Reply($"Не разобрал сумму '{tokens[1]}'.");
            return;
        }
        if (!ScheduleSpecParser.TryParse(tokens[2], out var schedule, out var scheduleError))
        {
            ctx.Reply(scheduleError);
            return;
        }

        Category? category = null;
        if (tokens.Length >= 4)
        {
            if (!CategoryExtensions.TryParse(tokens[3], out var cat))
            {
                ctx.Reply($"Неизвестная категория '{tokens[3]}'.");
                return;
            }
            category = cat;
        }

        Dispatch(ctx, shard, userId, new AddTemplate(userId, name, amount, schedule!, category));
    }

    private static void Dispatch(TelegramCommandContext ctx, IActorRef shard, Guid userId, object command)
        => ctx.AskShardAndReplyText(shard, userId, command, reply => reply switch
        {
            TemplateAdded a => $"Шаблон '{a.Template.Name}' добавлен ({Format(a.Template.Amount)} ₽, {a.Template.Schedule.Format()}{(a.Template.Category is { } c ? ", " + c : string.Empty)}).",
            TemplateRemoved r => $"Шаблон '{r.Name}' удалён.",
            TemplateRejected rj => $"Не удалось: {rj.Reason}",
            TemplateList list => FormatList(list),
            _ => "Не понял ответа."
        }, "Template");

    private static string FormatList(TemplateList list)
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

    private static string Format(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}
