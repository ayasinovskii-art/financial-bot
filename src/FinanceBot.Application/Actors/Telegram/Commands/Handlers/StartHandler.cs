using Akka.Event;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

/// <summary>
/// /start — регистрация (UUIDv5 от telegramId) и приветствие.
/// </summary>
public sealed class StartHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Start;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var cmd = new RegisterUser(userId, ctx.Update.TelegramId, ctx.Defaults.Timezone);
        var firstName = ctx.Update.FirstName ?? "друг";
        var chatId = ctx.Update.ChatId;
        var telegramId = ctx.Update.TelegramId;
        var log = ctx.Log;

        ctx.AskAndDispatch(shard, new ShardEnvelope(userId.ToString("N"), cmd), (reply, exc) =>
        {
            if (exc is not null)
            {
                log.Error(exc, "Register failed for telegramId={TelegramId}", telegramId);
                return [new OutgoingTelegramReply(chatId, "Не удалось зарегистрировать. Попробуй позже.")];
            }
            return reply switch
            {
                UserRegistrationCompleted => [new OutgoingTelegramReply(chatId, TelegramReplies.Welcome(firstName))],
                UserAlreadyRegistered => [new OutgoingTelegramReply(chatId, TelegramReplies.AlreadyRegistered())],
                _ => []
            };
        });
    }
}
