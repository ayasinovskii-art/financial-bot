using FinanceBot.Application.Actors.Telegram.Messages;
using Telegram.Bot.Types;

namespace FinanceBot.Infrastructure.Telegram;

/// <summary>
/// Конвертация Telegram.Bot.Types.Update → внутренние IncomingTelegramUpdate / IncomingCallbackQuery.
/// Используется как long-polling (TelegramBotAdapter), так и webhook (TelegramWebhookEndpoint в Host).
/// </summary>
public static class TelegramUpdateConverter
{
    public static (IncomingTelegramUpdate? Update, IncomingCallbackQuery? Callback) TryConvert(Update update)
    {
        if (update.Message is { From: { } messageFrom } message)
        {
            return (new IncomingTelegramUpdate(
                UpdateId: update.Id,
                ChatId: message.Chat.Id,
                TelegramId: messageFrom.Id,
                Username: messageFrom.Username,
                FirstName: messageFrom.FirstName,
                LastName: messageFrom.LastName,
                Text: message.Text,
                SentAt: new DateTimeOffset(message.Date, TimeSpan.Zero)), null);
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
                SentAt: DateTimeOffset.UtcNow));
        }

        return (null, null);
    }
}
