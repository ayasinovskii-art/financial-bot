using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class SavingsHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Savings;

    public void Execute(TelegramCommandContext ctx)
    {
        if (!AmountTextParser.TryParseAmount(ctx.ArgumentLine.Trim(), out var amount))
        {
            ctx.Reply(TelegramReplies.SavingsUsage());
            return;
        }

        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var cmd = new ConfirmSavings(userId, Guid.Empty, amount);
        ctx.AskShardAndReplyText(shard, userId, cmd, reply => reply switch
        {
            SavingsAccepted a => $"Накопления записаны: {a.Amount:0.00} ₽ для текущего периода.",
            SavingsRejected r => $"Не удалось: {r.Reason}",
            _ => "Не понял ответа."
        }, "Savings");
    }
}
