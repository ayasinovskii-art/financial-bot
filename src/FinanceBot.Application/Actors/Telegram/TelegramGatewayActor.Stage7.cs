using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Telegram.Commands;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Csv;
using FinanceBot.Application.Telegram;

namespace FinanceBot.Application.Actors.Telegram;

public sealed partial class TelegramGatewayActor
{
    partial void WireStage7()
    {
        _documentInterceptor = TryHandleCsvDocument;
        Receive<CsvDownloadResult>(OnCsvDownloadResult);
    }

    private bool TryHandleCsvDocument(TelegramCommandContext ctx)
    {
        var update = ctx.Update;
        var isCsv = string.Equals(update.DocumentMimeType, "text/csv", StringComparison.OrdinalIgnoreCase)
            || update.DocumentFileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true;

        if (!isCsv || update.DocumentFileId is null)
            return false;

        var fileId = update.DocumentFileId;
        var chatId = update.ChatId;
        var userId = UserIdFromTelegramId.Resolve(update.TelegramId);
        var correlationId = Guid.NewGuid();
        var self = Self;

        _telegramBot.DownloadFileAsync(fileId, CancellationToken.None)
            .ContinueWith(t => new CsvDownloadResult(
                correlationId, chatId, userId,
                t.IsCompletedSuccessfully ? t.Result : null,
                t.IsFaulted ? t.Exception : null))
            .PipeTo(self);

        return true;
    }

    private void OnCsvDownloadResult(CsvDownloadResult msg)
    {
        if (msg.Exception is not null || msg.Bytes is null)
        {
            _log.Warning("CSV download failed: {Exception}", msg.Exception?.Message);
            Self.Tell(new OutgoingTelegramReply(msg.ChatId, TelegramReplies.CsvImportDownloadFailed()));
            return;
        }

        string csvText;
        try
        {
            csvText = System.Text.Encoding.UTF8.GetString(msg.Bytes);
        }
        catch (Exception ex)
        {
            _log.Warning("CSV decode error: {Error}", ex.Message);
            Self.Tell(new OutgoingTelegramReply(msg.ChatId, TelegramReplies.CsvImportDownloadFailed()));
            return;
        }

        CsvParseResult result;
        try
        {
            result = _csvParser.Parse(csvText);
        }
        catch (Exception ex)
        {
            _log.Warning("CSV parse error: {Error}", ex.Message);
            Self.Tell(new OutgoingTelegramReply(msg.ChatId, TelegramReplies.CsvImportDownloadFailed()));
            return;
        }

        if (result.Rows.Count == 0)
        {
            Self.Tell(new OutgoingTelegramReply(msg.ChatId, TelegramReplies.CsvImportEmptyFile()));
            return;
        }

        _importPendingCache.Set(msg.CorrelationId,
            new ImportPendingEntry(msg.ChatId, msg.UserId, result.Rows, DateTimeOffset.UtcNow));

        var summaryText = TelegramReplies.CsvImportParseSummary(result.Rows.Count, result.SkippedCount);
        var buttons = new List<IReadOnlyList<InlineButton>>
        {
            new List<InlineButton>
            {
                new(TelegramReplies.CsvImportConfirmButton,
                    CallbackPayload.Encode("csvimport", msg.CorrelationId, "y")),
                new(TelegramReplies.CsvImportCancelButton,
                    CallbackPayload.Encode("csvimport", msg.CorrelationId, "n"))
            }
        };

        Self.Tell(new OutgoingInlineKeyboard(msg.ChatId, summaryText, buttons));
    }

    private sealed record CsvDownloadResult(
        Guid CorrelationId,
        long ChatId,
        Guid UserId,
        byte[]? Bytes,
        AggregateException? Exception);
}
