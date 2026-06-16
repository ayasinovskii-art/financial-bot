using Akka.Actor;
using Akka.Hosting;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

public sealed class NlpClarifyCallbackHandler : ITelegramCallbackHandler
{
    public const string ActionPrefix = "nlpc";
    private readonly NlpPendingCache _cache;

    public NlpClarifyCallbackHandler(NlpPendingCache cache) => _cache = cache;

    public string DataPrefix => ActionPrefix + ":";

    public void Execute(TelegramCallbackContext ctx)
    {
        if (!CallbackPayload.TryDecode(ctx.Callback.Data, out _, out var confirmationId, out var shortArg))
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Не понял callback."), ActorRefs.NoSender);
            return;
        }

        if (!_cache.TryRemove(confirmationId, out var entry) || entry is null)
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Сессия устарела, введите заново."), ActorRefs.NoSender);
            return;
        }

        if (shortArg == "n")
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Отменено."), ActorRefs.NoSender);
            return;
        }

        if (shortArg != "y" || entry.ParsedResult is null)
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Не понял ответ."), ActorRefs.NoSender);
            return;
        }

        var parsed = entry.ParsedResult;
        var registry = ActorRegistry.For(ctx.System);
        if (!registry.TryGet<UserShardMarker>(out var shard))
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Внутренняя ошибка."), ActorRefs.NoSender);
            return;
        }

        var callbackId = ctx.Callback.CallbackQueryId;
        var chatId = ctx.Callback.ChatId;
        var userId = entry.UserId;
        var category = parsed.Category;
        var self = ctx.Self;
        var askTimeout = ctx.AskTimeout;

        if (parsed.Type == "income")
        {
            var cmd = new ReportIncome(userId, parsed.Amount, DateTimeOffset.UtcNow, parsed.Description);
            shard.Ask<object>(new ShardEnvelope(userId.ToString("N"), cmd), askTimeout)
                .ContinueWith(t => BuildClarifyCompletion(t, callbackId, chatId, category,
                    r => r is IncomeAccepted a ? TelegramReplies.IncomeAccepted(a) : null))
                .PipeTo(self);
        }
        else
        {
            var cmd = new ReportExpense(userId, parsed.Amount, DateTimeOffset.UtcNow, parsed.Description, ExpenseSource.Claude);
            shard.Ask<object>(new ShardEnvelope(userId.ToString("N"), cmd), askTimeout)
                .ContinueWith(t => BuildClarifyCompletion(t, callbackId, chatId, category,
                    r => r is ExpenseAccepted a ? TelegramReplies.ExpenseAccepted(a) : null))
                .PipeTo(self);
        }
    }

    private static TelegramCommandCompleted BuildClarifyCompletion(
        Task<object> t,
        string callbackId,
        long chatId,
        string category,
        Func<object, string?> formatDetail)
    {
        var outgoing = new List<object>();

        if (!t.IsCompletedSuccessfully)
        {
            outgoing.Add(new OutgoingCallbackAck(callbackId, "Внутренняя ошибка."));
            return new TelegramCommandCompleted(outgoing);
        }

        outgoing.Add(new OutgoingCallbackAck(callbackId, "Записано."));
        var detail = formatDetail(t.Result);
        if (detail is not null)
            outgoing.Add(new OutgoingTelegramReply(chatId, TelegramReplies.NlpRecordedByAI(category) + "\n" + detail));

        return new TelegramCommandCompleted(outgoing);
    }
}
