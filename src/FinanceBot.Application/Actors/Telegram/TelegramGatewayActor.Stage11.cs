using System.Globalization;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram;

/// <summary>
/// Stage 11: команда /correct (список последних трат, требующих ручной категоризации,
/// с inline-клавиатурой) и обработка callback'ов для применения коррекции.
/// </summary>
public sealed partial class TelegramGatewayActor
{
    private const string CorrectCallbackPrefix = "correct:";
    private const int CorrectListLimit = 10;

    partial void WireStage11()
    {
        Receive<NeedsReviewReplyResult>(OnNeedsReviewReplyResult);
        Receive<CorrectionReplyResult>(OnCorrectionReplyResult);
    }

    partial void HandleCorrect(IncomingTelegramUpdate update, AccessDecision.Allowed allowed)
    {
        _ = allowed;
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var userShard))
        {
            _log.Error("UserShardRegion not registered.");
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var envelope = new ShardEnvelope(userId.ToString("N"),
            new GetNeedsReviewExpenses(userId, CorrectListLimit));

        var self = Self;
        userShard.Ask<object>(envelope, AskTimeout)
            .ContinueWith(t => (NeedsReviewReplyResult)new NeedsReviewReplyResult(update,
                t.IsCompletedSuccessfully ? t.Result as NeedsReviewList : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);
    }

    partial void HandleCorrectionCallback(IncomingCallbackQuery callback)
    {
        if (!callback.Data.StartsWith(CorrectCallbackPrefix, StringComparison.Ordinal))
        {
            _log.Debug("Unknown callback prefix: {Data}", callback.Data);
            Self.Tell(new OutgoingCallbackAck(callback.CallbackQueryId, null));
            return;
        }

        var rest = callback.Data[CorrectCallbackPrefix.Length..];
        var parts = rest.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var expenseId)
            || !CategoryExtensions.TryParse(parts[1], out var category))
        {
            _log.Warning("Bad correct callback payload: {Data}", callback.Data);
            Self.Tell(new OutgoingCallbackAck(callback.CallbackQueryId, "Не понял callback."));
            return;
        }

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<AccessControlSingletonMarker>(out var accessControl))
        {
            return;
        }

        var self = Self;
        accessControl.Ask<AccessDecision>(new Domain.Commands.AccessControl.IsAllowed(callback.TelegramId), AskTimeout)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully || t.Result is not AccessDecision.Allowed)
                {
                    self.Tell(new OutgoingCallbackAck(callback.CallbackQueryId, "Доступ ограничен."));
                    return;
                }

                if (!registry.TryGet<UserShardMarker>(out var userShard))
                {
                    return;
                }

                var userId = UserIdFromTelegramId.Resolve(callback.TelegramId);
                var cmd = new CorrectExpenseCategory(userId, expenseId, category);
                var env = new ShardEnvelope(userId.ToString("N"), cmd);

                userShard.Ask<object>(env, AskTimeout)
                    .ContinueWith(rt => (CorrectionReplyResult)new CorrectionReplyResult(callback,
                        rt.IsCompletedSuccessfully ? rt.Result : null,
                        rt.IsFaulted ? rt.Exception : null))
                    .PipeTo(self);
            });
    }

    private void OnNeedsReviewReplyResult(NeedsReviewReplyResult msg)
    {
        if (msg.Exception is not null || msg.List is null)
        {
            _log.Error(msg.Exception, "GetNeedsReview failed for telegramId={TelegramId}.", msg.Update.TelegramId);
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Внутренняя ошибка. Попробуй позже."));
            return;
        }

        if (msg.List.Expenses.Count == 0)
        {
            Self.Tell(new OutgoingTelegramReply(msg.Update.ChatId, "Все траты текущего периода уже категоризованы."));
            return;
        }

        foreach (var exp in msg.List.Expenses)
        {
            var text = FormatNeedsReview(exp);
            var rows = BuildCategoryKeyboard(exp.ExpenseId);
            Self.Tell(new OutgoingInlineKeyboard(msg.Update.ChatId, text, rows));
        }
    }

    private void OnCorrectionReplyResult(CorrectionReplyResult msg)
    {
        var ackText = msg.Reply switch
        {
            ExpenseCorrectionApplied a => $"OK: {a.OldCategory} → {a.NewCategory}",
            ExpenseCorrectionRejected r => r.Reason,
            _ => msg.Exception is null ? "OK" : "Внутренняя ошибка."
        };
        Self.Tell(new OutgoingCallbackAck(msg.Callback.CallbackQueryId, ackText));

        if (msg.Reply is ExpenseCorrectionApplied applied)
        {
            Self.Tell(new OutgoingTelegramReply(msg.Callback.ChatId,
                $"Категория обновлена: {applied.OldCategory} → {applied.NewCategory}. Запомнил для будущих трат с тем же описанием."));
        }
    }

    private static string FormatNeedsReview(FinanceBot.Application.Actors.User.NeedsReviewExpense exp)
    {
        var sb = new StringBuilder();
        sb.Append('#').Append(exp.ExpenseId.ToString("N")[..6]).Append(' ');
        sb.Append(exp.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(" ₽ — ");
        sb.Append(string.IsNullOrWhiteSpace(exp.Description) ? "(без описания)" : exp.Description);
        sb.Append("  [текущая: ").Append(exp.Category).Append(']');
        return sb.ToString();
    }

    private static IReadOnlyList<IReadOnlyList<InlineButton>> BuildCategoryKeyboard(Guid expenseId)
    {
        // 13 категорий → 5 строк по 3 кнопки + одна по 1.
        var keys = new[]
        {
            Category.Groceries, Category.DiningOut, Category.Transport,
            Category.Utilities, Category.Subscriptions, Category.Entertainment,
            Category.Health, Category.Clothing, Category.Personal,
            Category.Education, Category.Gifts, Category.Travel,
            Category.Other
        };
        var rows = new List<IReadOnlyList<InlineButton>>();
        for (var i = 0; i < keys.Length; i += 3)
        {
            var row = new List<InlineButton>();
            for (var j = i; j < Math.Min(i + 3, keys.Length); j++)
            {
                row.Add(new InlineButton(keys[j].ToString(),
                    $"correct:{expenseId:N}:{keys[j]}"));
            }
            rows.Add(row);
        }
        return rows;
    }

    private sealed record NeedsReviewReplyResult(IncomingTelegramUpdate Update, NeedsReviewList? List, AggregateException? Exception);
    private sealed record CorrectionReplyResult(IncomingCallbackQuery Callback, object? Reply, AggregateException? Exception);
}
