using Akka.Actor;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Stage 19: команда /advice [week|month]. Fire-and-forget — ответ от UserActor
/// приходит через EventStream (OutgoingTelegramReply), а не через Ask.
/// </summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage19()
    {
        // Дополнительные сообщения не требуются — UserActor публикует reply через EventStream.
    }

    partial void HandleAdvice(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var scope = string.IsNullOrWhiteSpace(args) ? null : args.Trim();
        var cmd = new RequestConsultation(userId, Prompt: string.Empty, Scope: scope);
        userShard.Tell(new ShardEnvelope(userId.ToString("N"), cmd));

        Self.Tell(new OutgoingTelegramReply(update.ChatId,
            scope is null
                ? "Готовлю совет…"
                : $"Готовлю {scope}-обзор…"));
    }
}
