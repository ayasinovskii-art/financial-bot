using FinanceBot.Application.Actors.Telegram.Messages;
using Telegram.Bot.Types;

namespace FinanceBot.Infrastructure.Telegram;

/// <summary>
/// Конвертация Telegram.Bot.Types.Update → внутренние IncomingTelegramUpdate / IncomingCallbackQuery /
/// IncomingTelegramFile. Используется как long-polling (TelegramBotAdapter), так и webhook
/// (TelegramWebhookEndpoint в Host).
/// </summary>
public static class TelegramUpdateConverter
{
    public static (IncomingTelegramUpdate? Update, IncomingCallbackQuery? Callback, IncomingTelegramFile? File) TryConvert(Update update)
    {
        if (update.Message is { From: { } messageFrom } message)
        {
            var sentAt = new DateTimeOffset(message.Date, TimeSpan.Zero);

            // Фото (скриншот выписки) — берём наибольшее разрешение (последний PhotoSize).
            if (message.Photo is { Length: > 0 } photos)
            {
                var largest = photos[^1];
                return (null, null, new IncomingTelegramFile(
                    UpdateId: update.Id,
                    ChatId: message.Chat.Id,
                    TelegramId: messageFrom.Id,
                    Username: messageFrom.Username,
                    FirstName: messageFrom.FirstName,
                    LastName: messageFrom.LastName,
                    FileId: largest.FileId,
                    Kind: FileKind.Photo,
                    MimeType: "image/jpeg",
                    Caption: message.Caption,
                    SentAt: sentAt));
            }

            // Документ (CSV / PDF выписка).
            if (message.Document is { } document)
            {
                return (null, null, new IncomingTelegramFile(
                    UpdateId: update.Id,
                    ChatId: message.Chat.Id,
                    TelegramId: messageFrom.Id,
                    Username: messageFrom.Username,
                    FirstName: messageFrom.FirstName,
                    LastName: messageFrom.LastName,
                    FileId: document.FileId,
                    Kind: FileKind.Document,
                    MimeType: document.MimeType,
                    Caption: message.Caption,
                    SentAt: sentAt));
            }

            return (new IncomingTelegramUpdate(
                UpdateId: update.Id,
                ChatId: message.Chat.Id,
                TelegramId: messageFrom.Id,
                Username: messageFrom.Username,
                FirstName: messageFrom.FirstName,
                LastName: messageFrom.LastName,
                Text: message.Text,
                SentAt: sentAt), null, null);
        }

        if (update.CallbackQuery is { Data: { } data, Message: { } cbMessage } cb)
        {
            return (null, new IncomingCallbackQuery(
                UpdateId: update.Id,
                CallbackQueryId: cb.Id,
                ChatId: cbMessage.Chat.Id,
                TelegramId: cb.From.Id,
                Username: cb.From.Username,
                FirstName: cb.From.FirstName,
                Data: data,
                SentAt: DateTimeOffset.UtcNow), null);
        }

        return (null, null, null);
    }
}
