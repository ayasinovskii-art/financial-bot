using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>Stage 9: команды /expense, /expense_day и парсинг свободного текста.</summary>
public sealed partial class TelegramGatewayActor
{
    partial void WireStage9()
    {
        Receive<ExpenseReplyResult>(OnExpenseReplyResult);
    }

    partial void HandleExpense(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var parsed = AmountTextParser.TryParseSingle(args);
        if (parsed is null)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.ExpenseUsage()));
            return;
        }
        if (string.IsNullOrWhiteSpace(parsed.Description))
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId,
                "Для /expense нужно описание. Используй /expense_day для итога дня."));
            return;
        }
        DispatchExpense(update, parsed.Date, parsed.Amount, parsed.Description!);
    }

    partial void HandleExpenseDay(IncomingTelegramUpdate update, string args, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var parsed = AmountTextParser.TryParseSingle(args);
        if (parsed is null)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId,
                "Формат: `/expense_day [<YYYY-MM-DD>] <amount>`."));
            return;
        }
        DispatchExpense(update, parsed.Date, parsed.Amount, parsed.Description ?? "(итог дня)");
    }

    partial void HandleFreeText(IncomingTelegramUpdate update, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        if (string.IsNullOrWhiteSpace(update.Text))
        {
            return;
        }

        var parts = AmountTextParser.ParseMultiple(update.Text);
        if (parts.Count == 0)
        {
            Self.Tell(new OutgoingTelegramReply(update.ChatId, TelegramReplies.UnknownCommand()));
            return;
        }

        foreach (var part in parts)
        {
            DispatchExpense(update, part.Date, part.Amount,
                string.IsNullOrWhiteSpace(part.Description) ? "(без описания)" : part.Description!);
        }
    }

    private void DispatchExpense(IncomingTelegramUpdate update, DateOnly? date, decimal amount, string description)
    {
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            _log.Error("UserShardRegion not registered.");
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var occurredAt = date is { } d
            ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var cmd = new ReportExpense(userId, amount, occurredAt, description, ExpenseSource.Manual);
        var envelope = new ShardEnvelope(userId.ToString("N"), cmd);

        var self = Self;
        userShard.Ask<object>(envelope, AskTimeout)
            .ContinueWith(t => (ExpenseReplyResult)new ExpenseReplyResult(update,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    private void OnExpenseReplyResult(ExpenseReplyResult msg)
    {
        if (msg.Exception is not null)
        {
            _log.Error(msg.Exception, "Expense command failed for telegramId={TelegramId}.", msg.Update.TelegramId);
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка. Попробуй позже."));
            return;
        }

        var text = msg.Reply switch
        {
            ExpenseAccepted a => TelegramReplies.ExpenseAccepted(a),
            ExpenseRejected r => $"Не удалось записать трату: {r.Reason}",
            _ => "Не понял ответа от UserActor."
        };
        Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, text));
    }

    private sealed record ExpenseReplyResult(IncomingTelegramUpdate Update, object? Reply, AggregateException? Exception);
}
