using Akka.Actor;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Configuration;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class HelpHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Help;
    public void Execute(TelegramCommandContext ctx) => ctx.Reply(TelegramReplies.Help());
}

public sealed class WhoAmIHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.WhoAmI;
    public void Execute(TelegramCommandContext ctx) => ctx.Reply(TelegramReplies.WhoAmI(ctx.Update.TelegramId));
}

public sealed class CancelHandler : ITelegramCommandHandler
{
    public TelegramCommandKind Kind => TelegramCommandKind.Cancel;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        shard.Tell(new ShardEnvelope(userId.ToString("N"),
            new FinanceBot.Domain.Commands.User.Cancel(userId)));

        ctx.Reply(TelegramReplies.CancelAck());
    }
}
