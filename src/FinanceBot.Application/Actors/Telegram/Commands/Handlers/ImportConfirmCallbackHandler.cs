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

public sealed class ImportConfirmCallbackHandler : ITelegramCallbackHandler
{
    public const string ActionPrefix = "csvimport";
    private readonly ImportPendingCache _cache;

    public ImportConfirmCallbackHandler(ImportPendingCache cache) => _cache = cache;

    public string DataPrefix => ActionPrefix + ":";

    public void Execute(TelegramCallbackContext ctx)
    {
        if (!CallbackPayload.TryDecode(ctx.Callback.Data, out _, out var correlationId, out var shortArg))
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, TelegramReplies.CsvImportUnknownCallback()), ActorRefs.NoSender);
            return;
        }

        if (shortArg == "n")
        {
            _cache.TryRemove(correlationId, out _);
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, null), ActorRefs.NoSender);
            ctx.Self.Tell(new OutgoingTelegramReply(ctx.Callback.ChatId, TelegramReplies.CsvImportCancelled()), ActorRefs.NoSender);
            return;
        }

        if (shortArg != "y" || !_cache.TryRemove(correlationId, out var entry) || entry is null)
        {
            var msg = shortArg != "y"
                ? TelegramReplies.CsvImportUnknownAnswer()
                : TelegramReplies.CsvImportSessionExpired();
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, msg), ActorRefs.NoSender);
            return;
        }

        var registry = ActorRegistry.For(ctx.System);
        if (!registry.TryGet<UserShardMarker>(out var shard))
        {
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, TelegramReplies.CsvImportInternalError()), ActorRefs.NoSender);
            return;
        }

        var rows = entry.Rows
            .Select(r => new BulkExpenseRow(r.Amount, r.Date, r.Description))
            .ToList();

        var cmd = new BulkAddExpenses(entry.UserId, Guid.Empty, rows);
        var callbackId = ctx.Callback.CallbackQueryId;
        var chatId = ctx.Callback.ChatId;
        var self = ctx.Self;

        shard.Ask<object>(new ShardEnvelope(entry.UserId.ToString("N"), cmd), ctx.AskTimeout)
            .ContinueWith(t => BuildCompletion(t, callbackId, chatId))
            .PipeTo(self);
    }

    private static TelegramCommandCompleted BuildCompletion(Task<object> t, string callbackId, long chatId)
    {
        var outgoing = new List<object>();

        if (!t.IsCompletedSuccessfully)
        {
            outgoing.Add(new OutgoingCallbackAck(callbackId, TelegramReplies.CsvImportInternalError()));
            return new TelegramCommandCompleted(outgoing);
        }

        outgoing.Add(new OutgoingCallbackAck(callbackId, null));

        switch (t.Result)
        {
            case BulkExpensesResult r:
                outgoing.Add(new OutgoingTelegramReply(chatId, TelegramReplies.CsvImportSuccess(r.Added, r.Skipped)));
                break;
            case BulkExpensesRejected rej:
                outgoing.Add(new OutgoingTelegramReply(chatId, TelegramReplies.CsvImportRejected(rej.Reason)));
                break;
            default:
                outgoing.Add(new OutgoingTelegramReply(chatId, TelegramReplies.CsvImportUnexpectedResponse()));
                break;
        }

        return new TelegramCommandCompleted(outgoing);
    }
}
