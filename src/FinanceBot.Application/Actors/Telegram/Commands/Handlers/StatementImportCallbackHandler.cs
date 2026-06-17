using System.Globalization;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.AccessControl;
using FinanceBot.Application.Actors.StatementImport;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Telegram.Commands.Handlers;

/// <summary>
/// Обрабатывает inline-кнопки подтверждения импорта выписки: <c>import:confirm:&lt;proposalId&gt;</c>,
/// <c>import:cancel:&lt;proposalId&gt;</c>, <c>import:list:&lt;proposalId&gt;</c>.
/// </summary>
public sealed class StatementImportCallbackHandler : ITelegramCallbackHandler
{
    public string DataPrefix => StatementImportActor.CallbackPrefix;

    public void Execute(TelegramCallbackContext ctx)
    {
        var rest = ctx.Callback.Data[DataPrefix.Length..];
        var parts = rest.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[1], "N", out var proposalId))
        {
            ctx.Log.Warning("Bad import callback payload: {Data}", ctx.Callback.Data);
            ctx.Self.Tell(new OutgoingCallbackAck(ctx.Callback.CallbackQueryId, "Не понял callback."));
            return;
        }
        var action = parts[0];

        var registry = ActorRegistry.For(ctx.System);
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
                object command = action switch
                {
                    "confirm" => new ConfirmStatementImport(userId, proposalId),
                    "list" => new GetPendingStatementImport(userId),
                    _ => new CancelStatementImport(userId)
                };
                var env = new ShardEnvelope(userId.ToString("N"), command);

                userShard.Ask<object>(env, askTimeout)
                    .ContinueWith(rt =>
                    {
                        var (ack, body) = rt.IsCompletedSuccessfully
                            ? FormatReply(rt.Result, chatId)
                            : ("Внутренняя ошибка.", null);

                        var outgoing = new List<object>(2) { new OutgoingCallbackAck(callbackId, ack) };
                        if (body is not null)
                        {
                            outgoing.Add(body);
                        }
                        return new TelegramCommandCompleted(outgoing);
                    })
                    .PipeTo(self);
            });
    }

    private static (string Ack, object? Body) FormatReply(object reply, long chatId) => reply switch
    {
        StatementImportCompleted c => (
            $"Импортировано {c.Imported}" + (c.SkippedDuplicates > 0 ? $", дублей {c.SkippedDuplicates}" : string.Empty),
            new OutgoingTelegramReply(chatId,
                $"Готово: импортировано {c.Imported} транзакц(ий) — расходы {c.ExpenseTotal:0.00} ₽, доходы {c.IncomeTotal:0.00} ₽."
                + (c.SkippedDuplicates > 0 ? $" Пропущено дублей: {c.SkippedDuplicates}." : string.Empty))),
        StatementImportCancelled => ("Отменено", new OutgoingTelegramReply(chatId, "Импорт отменён.")),
        StatementImportRejected r => (r.Reason, new OutgoingTelegramReply(chatId, r.Reason)),
        StatementImportList { Transactions.Count: 0 } => ("Список пуст", new OutgoingTelegramReply(chatId, "Нет ожидающего импорта.")),
        StatementImportList l => ("Список", new OutgoingTelegramReply(chatId, FormatList(l.Transactions))),
        _ => ("OK", null)
    };

    private static string FormatList(IReadOnlyList<ImportedTransaction> transactions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Распознанные транзакции:");
        foreach (var t in transactions.Take(50))
        {
            var sign = t.Kind == TransactionKind.Income ? "+" : "−";
            sb.Append(sign).Append(' ')
              .Append(t.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(" ₽  ")
              .Append(t.Date.ToString("dd.MM", CultureInfo.InvariantCulture)).Append("  ")
              .AppendLine(t.Description);
        }
        return sb.ToString().TrimEnd();
    }
}
