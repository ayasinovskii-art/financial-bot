using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>Stage 8: команда /income.</summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage8()
    {
        Receive<IncomeReplyResult>(OnIncomeReplyResult);
    }

    partial void HandleIncome(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            _log.Error("UserShardRegion not registered.");
            return;
        }

        var parsed = AmountTextParser.TryParseSingle(args);
        if (parsed is null)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.IncomeUsage()));
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var occurredAt = parsed.Date is { } d
            ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var cmd = new ReportIncome(userId, parsed.Amount, occurredAt, parsed.Description);
        var envelope = new ShardEnvelope(userId.ToString("N"), cmd);

        var self = Self;
        userShard.Ask<object>(envelope, AskTimeout)
            .ContinueWith(t => (IncomeReplyResult)new IncomeReplyResult(update,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnIncomeReplyResult(IncomeReplyResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Income command failed for telegramId={TelegramId}.", msg.Update.TelegramId);
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка. Попробуй позже."));
            return;
        }

        var text = msg.Reply switch
        {
            IncomeAccepted a => TelegramReplies.IncomeAccepted(a),
            IncomeRejected r => $"Не удалось записать доход: {r.Reason}",
            _ => "Не понял ответа от UserActor."
        };
        Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, text));
    }

    private sealed record IncomeReplyResult(IncomingTelegramUpdate Update, object? Reply, AggregateException? Exception);
}
