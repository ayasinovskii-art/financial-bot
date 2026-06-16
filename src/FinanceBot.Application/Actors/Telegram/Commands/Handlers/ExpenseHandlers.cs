using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

internal static class ExpenseShared
{
    public static void Dispatch(TelegramCommandContext ctx, DateOnly? date, decimal amount, string description,
        ExpenseSource source = ExpenseSource.Manual)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var occurredAt = date is { } d
            ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var cmd = new ReportExpense(userId, amount, occurredAt, description, source);
        ctx.AskShardAndReplyText(shard, userId, cmd, reply => reply switch
        {
            ExpenseAccepted a => TelegramReplies.ExpenseAccepted(a),
            ExpenseRejected r => $"Не удалось записать трату: {r.Reason}",
            _ => "Не понял ответа от UserActor."
        }, "Expense");
    }
}

public sealed class ExpenseHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Expense;

    public void Execute(TelegramCommandContext ctx)
    {
        var parsed = AmountTextParser.TryParseSingle(ctx.ArgumentLine);
        if (parsed is null)
        {
            ctx.Reply(TelegramReplies.ExpenseUsage());
            return;
        }
        if (string.IsNullOrWhiteSpace(parsed.Description))
        {
            ctx.Reply("Для /expense нужно описание. Используй /expense_day для итога дня.");
            return;
        }
        ExpenseShared.Dispatch(ctx, parsed.Date, parsed.Amount, parsed.Description!);
    }
}

public sealed class ExpenseDayHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.ExpenseDay;

    public void Execute(TelegramCommandContext ctx)
    {
        var parsed = AmountTextParser.TryParseSingle(ctx.ArgumentLine);
        if (parsed is null)
        {
            ctx.Reply("Формат: `/expense_day [<YYYY-MM-DD>] <amount>`.");
            return;
        }
        ExpenseShared.Dispatch(ctx, parsed.Date, parsed.Amount, parsed.Description ?? "(итог дня)");
    }
}

/// <summary>Парсер свободного текста (не команда — диспатчит как /expense через <see cref="ExpenseShared"/>).</summary>
internal static class FreeTextHandler
{
    public static void Execute(TelegramCommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Update.Text))
        {
            return;
        }

        var parts = AmountTextParser.ParseMultiple(ctx.Update.Text);
        if (parts.Count == 0)
        {
            ctx.Reply(TelegramReplies.UnknownCommand());
            return;
        }

        foreach (var part in parts)
        {
            ExpenseShared.Dispatch(ctx, part.Date, part.Amount,
                string.IsNullOrWhiteSpace(part.Description) ? "(без описания)" : part.Description!);
        }
    }
}
