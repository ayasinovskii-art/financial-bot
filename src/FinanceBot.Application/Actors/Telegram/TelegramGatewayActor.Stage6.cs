using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.Claude;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Commands.Handlers;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Telegram;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram;

public sealed partial class TelegramGatewayActor
{
    private const double NlpConfidenceThreshold = 0.85;

    partial void WireStage6()
    {
        _freeTextNlpInterceptor = TryHandleFreeTextWithNlp;
        Receive<ClaudeOkReply>(HandleClaudeNlpOkReply);
        Receive<ClaudeUnavailableReply>(HandleClaudeNlpUnavailableReply);
    }

    private bool TryHandleFreeTextWithNlp(TelegramCommandContext ctx)
    {
        if (!NlpPreGate.HasAmount(ctx.Update.Text ?? string.Empty))
            return false;

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<ClaudeConsultantSingletonMarker>(out var consultant))
            return false;

        var correlationId = Guid.NewGuid();
        var userId = UserIdFromTelegramId.Resolve(ctx.Update.TelegramId);
        _nlpPendingCache.Add(correlationId,
            new NlpPendingEntry(ctx.Update.ChatId, userId, ctx, null, DateTimeOffset.UtcNow));

        var request = NlpExpenseParser.BuildClaudeRequest(ctx.Update.Text!, correlationId);
        consultant.Tell(new ClaudeAskMessage(request), Self);
        return true;
    }

    private void HandleClaudeNlpOkReply(ClaudeOkReply reply)
    {
        if (!_nlpPendingCache.TryRemove(reply.CorrelationId, out var entry) || entry is null)
            return;

        if (!NlpExpenseParser.TryParseResponse(reply.Content, out var parsed) || parsed is null || !parsed.IsFinancial)
            return;

        if (parsed.Confidence >= NlpConfidenceThreshold)
        {
            DispatchHighConfidenceNlp(entry, parsed);
        }
        else
        {
            var confirmationId = Guid.NewGuid();
            _nlpPendingCache.Add(confirmationId,
                new NlpPendingEntry(entry.ChatId, entry.UserId, null, parsed, DateTimeOffset.UtcNow));

            var clarifyText = parsed.Type == "income"
                ? TelegramReplies.NlpClarifyIncome(parsed.Description, parsed.Amount)
                : TelegramReplies.NlpClarifyExpense(parsed.Description, parsed.Amount);

            var buttons = new List<IReadOnlyList<InlineButton>>
            {
                new List<InlineButton>
                {
                    new("✅ Да", CallbackPayload.Encode("nlpc", confirmationId, "y")),
                    new("❌ Нет", CallbackPayload.Encode("nlpc", confirmationId, "n"))
                }
            };

            Self.Tell(new OutgoingInlineKeyboard(entry.ChatId, clarifyText, buttons));
        }
    }

    private void HandleClaudeNlpUnavailableReply(ClaudeUnavailableReply reply)
    {
        if (!_nlpPendingCache.TryRemove(reply.CorrelationId, out var entry) || entry is null)
            return;

        if (entry.Context is not null)
            FreeTextHandler.Execute(entry.Context);
    }

    private void DispatchHighConfidenceNlp(NlpPendingEntry entry, NlpParseResult parsed)
    {
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<UserShardMarker>(out var shard))
        {
            _log.Warning("UserShard not available during NLP high-confidence dispatch.");
            Self.Tell(new OutgoingTelegramReply(entry.ChatId, "Внутренняя ошибка. Попробуй позже."));
            return;
        }

        var chatId = entry.ChatId;
        var userId = entry.UserId;
        var category = parsed.Category;
        var self = Self;

        if (parsed.Type == "income")
        {
            var cmd = new ReportIncome(userId, parsed.Amount, DateTimeOffset.UtcNow, parsed.Description);
            shard.Ask<object>(new ShardEnvelope(userId.ToString("N"), cmd), AskTimeout)
                .ContinueWith(t => BuildNlpCompletion(t, chatId, category,
                    r => r is IncomeAccepted a
                        ? TelegramReplies.NlpRecordedByAI(category) + "\n" + TelegramReplies.IncomeAccepted(a)
                        : r is IncomeRejected rej ? $"Не удалось записать доход: {rej.Reason}" : null))
                .PipeTo(self);
        }
        else
        {
            var cmd = new ReportExpense(userId, parsed.Amount, DateTimeOffset.UtcNow, parsed.Description, ExpenseSource.Claude);
            shard.Ask<object>(new ShardEnvelope(userId.ToString("N"), cmd), AskTimeout)
                .ContinueWith(t => BuildNlpCompletion(t, chatId, category,
                    r => r is ExpenseAccepted a
                        ? TelegramReplies.NlpRecordedByAI(category) + "\n" + TelegramReplies.ExpenseAccepted(a)
                        : r is ExpenseRejected rej ? $"Не удалось записать трату: {rej.Reason}" : null))
                .PipeTo(self);
        }
    }

    private static TelegramCommandCompleted BuildNlpCompletion(
        Task<object> t,
        long chatId,
        string category,
        Func<object, string?> formatReply)
    {
        if (!t.IsCompletedSuccessfully)
            return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, "Внутренняя ошибка. Попробуй позже.")]);

        var text = formatReply(t.Result) ?? TelegramReplies.NlpRecordedByAI(category);
        return new TelegramCommandCompleted([new OutgoingTelegramReply(chatId, text)]);
    }
}
