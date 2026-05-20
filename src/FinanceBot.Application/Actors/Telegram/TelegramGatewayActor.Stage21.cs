using Akka.Actor;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Stage 21: команда /report [current|previous|N]. Reply возвращается через EventStream.
/// </summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage21()
    {
    }

    partial void HandleReport(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            return;
        }
        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var period = string.IsNullOrWhiteSpace(args) ? null : args.Trim();
        var cmd = new RequestReport(userId, period);
        userShard.Tell(new ShardEnvelope(userId.ToString("N"), cmd));
    }
}
