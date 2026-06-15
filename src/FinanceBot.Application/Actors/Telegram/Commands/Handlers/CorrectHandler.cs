using System.Globalization;
using System.Text;
using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class CorrectHandler : ITelegramCommandHandler
{
    public const string CallbackPrefix = "correct:";
    private const int CorrectListLimit = 10;

    public TelegramCommandKind Kind => TelegramCommandKind.Correct;

    public void Execute(TelegramCommandContext ctx)
    {
        var shard = ctx.GetShard<UserShardMarker>();
        if (shard is null)
        {
            return;
        }

        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        var chatId = ctx.Update.ChatId;
        var log = ctx.Log;

        ctx.AskAndDispatch(shard,
            new ShardEnvelope(userId.ToString("N"), new GetNeedsReviewExpenses(userId, CorrectListLimit)),
            (reply, exc) =>
            {
                if (exc is not null)
                {
                    log.Error(exc, "GetNeedsReview failed.");
                    return [new OutgoingTelegramReply(chatId, "Внутренняя ошибка. Попробуй позже.")];
                }
                if (reply is not NeedsReviewList list)
                {
                    return [new OutgoingTelegramReply(chatId, "Внутренняя ошибка.")];
                }
                if (list.Expenses.Count == 0)
                {
                    return [new OutgoingTelegramReply(chatId, "Все траты текущего периода уже категоризованы.")];
                }
                var outgoing = new List<object>(list.Expenses.Count);
                foreach (var exp in list.Expenses)
                {
                    outgoing.Add(new OutgoingInlineKeyboard(chatId, FormatNeedsReview(exp), BuildCategoryKeyboard(exp.ExpenseId)));
                }
                return outgoing;
            });
    }

    internal static string FormatNeedsReview(NeedsReviewExpense exp)
    {
        var sb = new StringBuilder();
        sb.Append('#').Append(exp.ExpenseId.ToString("N")[..6]).Append(' ');
        sb.Append(exp.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(" ₽ — ");
        sb.Append(string.IsNullOrWhiteSpace(exp.Description) ? "(без описания)" : exp.Description);
        sb.Append("  [текущая: ").Append(exp.Category).Append(']');
        return sb.ToString();
    }

    internal static IReadOnlyList<IReadOnlyList<InlineButton>> BuildCategoryKeyboard(Guid expenseId)
    {
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
                row.Add(new InlineButton(keys[j].ToString(), CallbackPayload.Encode("correct", expenseId, keys[j].ToString())));
            }
            rows.Add(row);
        }
        return rows;
    }
}

public sealed class CorrectionCallbackHandler : ITelegramCallbackHandler
{
    public string DataPrefix => CorrectHandler.CallbackPrefix;

    public void Execute(TelegramCallbackContext ctx)
    {
        if (!CallbackPayload.TryDecode(ctx.Callback.Data, out _, out var expenseId, out var shortArg)
            || shortArg is null || !CategoryExtensions.TryParse(shortArg, out var category))
        {
            ctx.Log.Warning("Bad correct callback payload: {Data}", ctx.Callback.Data);
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Не понял callback."));
            return;
        }

        var registry = Akka.Hosting.ActorRegistry.For(ctx.System);
        if (!registry.TryGet<AccessControlSingletonMarker>(out var accessControl)
            || !registry.TryGet<UserShardMarker>(out var userShard))
        {
            return;
        }

        var self = ctx.Self;
        var callbackId = ctx.Callback.CallbackQueryId;
        var chatId = ctx.Callback.ChatId;
        var telegramId = ctx.Callback.TelegramId;
        var askTimeout = ctx.AskTimeout;

        accessControl.Ask<AccessDecision>(new Domain.Commands.AccessControl.IsAllowed(telegramId), askTimeout)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully || t.Result is not AccessDecision.Allowed)
                {
                    self.Tell(new OutgoingCallbackAck(callbackId, "Доступ ограничен."));
                    return;
                }

                var userId = UserIdFromTelegramId.Resolve(telegramId);
                var cmd = new CorrectExpenseCategory(userId, expenseId, category);
                var env = new ShardEnvelope(userId.ToString("N"), cmd);

                userShard.Ask<object>(env, askTimeout)
                    .ContinueWith(rt =>
                    {
                        var outgoing = new List<object>(2);
                        var ackText = rt.IsCompletedSuccessfully
                            ? rt.Result switch
                            {
                                ExpenseCorrectionApplied a => $"OK: {a.OldCategory} → {a.NewCategory}",
                                ExpenseCorrectionRejected r => r.Reason,
                                _ => "OK"
                            }
                            : "Внутренняя ошибка.";
                        outgoing.Add(new OutgoingCallbackAck(callbackId, ackText));

                        if (rt.IsCompletedSuccessfully)
                        {
                            if (rt.Result is ExpenseCorrectionApplied applied)
                            {
                                outgoing.Add(new OutgoingTelegramReply(chatId,
                                    $"Категория обновлена: {applied.OldCategory} → {applied.NewCategory}. Запомнил для будущих трат с тем же описанием."));
                            }
                        }

                        return new TelegramCommandCompleted(outgoing);
                    })
                    .PipeTo(self);
            });
    }
}
