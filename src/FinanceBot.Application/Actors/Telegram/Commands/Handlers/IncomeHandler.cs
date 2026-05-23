using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class IncomeHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Income;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var parsed = AmountTextParser.TryParseSingle(ctx.ArgumentLine);
        if (parsed is null)
        {
            ctx.Reply(TelegramReplies.IncomeUsage());
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var occurredAt = parsed.Date is { } d
            ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var cmd = new ReportIncome(userId, parsed.Amount, occurredAt, parsed.Description);
        ctx.AskShardAndReplyText(shard, userId, cmd, reply => reply switch
        {
            IncomeAccepted a => TelegramReplies.IncomeAccepted(a),
            IncomeRejected r => $"Не удалось записать доход: {r.Reason}",
            _ => "Не понял ответа от UserActor."
        }, "Income");
    }
}
