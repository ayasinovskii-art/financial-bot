using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>Stage 15: команда /savings. Закрытие периода — auto при следующем /income после /savings.</summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage15()
    {
        Receive<SavingsReplyResult>(OnSavingsReplyResult);
    }

    partial void HandleSavings(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        if (!AmountTextParser.TryParseAmount(args.Trim(), out var amount))
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.SavingsUsage()));
            return;
        }

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        // PeriodId = Guid.Empty — UserActor резолвит в active period.
        var cmd = new ConfirmSavings(userId, Guid.Empty, amount);
        var envelope = new ShardEnvelope(userId.ToString("N"), cmd);

        var self = Self;
        userShard.Ask<object>(envelope, AskTimeout)
            .ContinueWith(t => (SavingsReplyResult)new SavingsReplyResult(update,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnSavingsReplyResult(SavingsReplyResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Savings command failed.");
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка."));
            return;
        }

        var text = msg.Reply switch
        {
            SavingsAccepted a => $"Накопления записаны: {a.Amount:0.00} ₽ для текущего периода.",
            SavingsRejected r => $"Не удалось: {r.Reason}",
            _ => "Не понял ответа."
        };
        Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, text));
    }

    private sealed record SavingsReplyResult(IncomingTelegramUpdate Update, object? Reply, AggregateException? Exception);
}
