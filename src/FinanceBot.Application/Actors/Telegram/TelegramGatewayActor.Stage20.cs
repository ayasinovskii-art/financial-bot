using Akka.Actor;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Stage 20: команда /chart [category|daily|buckets|savings].
/// UserActor публикует PNG через EventStream(OutgoingTelegramPhoto), отдельного Ask нет.
/// </summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage20()
    {
    }

    partial void HandleChart(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var chartType = string.IsNullOrWhiteSpace(args) ? "category" : args.Trim();
        var cmd = new RequestChart(userId, chartType, Params: null);
        userShard.Tell(new ShardEnvelope(userId.ToString("N"), cmd));
    }
}
