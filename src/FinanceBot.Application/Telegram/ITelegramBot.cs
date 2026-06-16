using FinanceBot.Application.Actors.Telegram.Messages;

namespace FinanceBot.Application.Telegram;

/// <summary>
/// Тонкая абстракция над Telegram Bot API, чтобы Application не зависел напрямую от Telegram.Bot.
/// Реализация — TelegramBotAdapter в Infrastructure.
/// </summary>
public interface ITelegramBot
{
    /// <summary>Отправить текстовый ответ.</summary>
    Task SendTextAsync(long chatId, string text, CancellationToken ct);

    /// <summary>
    /// Отправить текст с inline-клавиатурой. Кнопки задаются построчно;
    /// <see cref="InlineButton.CallbackData"/> возвращается в <see cref="IncomingCallbackQuery.Data"/>.
    /// </summary>
    Task SendInlineKeyboardAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<InlineButton>> rows, CancellationToken ct);

    /// <summary>Подтвердить callback (убрать «часики» с кнопки).</summary>
    Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken ct);

    /// <summary>Отправить фото (PNG) с опциональной подписью.</summary>
    Task SendPhotoAsync(long chatId, byte[] photo, string fileName, string? caption, CancellationToken ct);

    /// <summary>Отправить документ (файл) с опциональной подписью.</summary>
    Task SendDocumentAsync(long chatId, byte[] document, string fileName, string? caption, CancellationToken ct);

    /// <summary>Скачать файл по fileId. Возвращает байты содержимого.</summary>
    Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct);

    /// <summary>Получить пачку обновлений (long-polling). Возвращает offset следующего вызова.</summary>
    Task<TelegramPollResult> PollAsync(long offset, TimeSpan timeout, CancellationToken ct);

    /// <summary>Установить webhook URL (alternative к polling).</summary>
    Task<bool> SetWebhookAsync(string url, CancellationToken ct);

    /// <summary>Удалить webhook.</summary>
    Task DeleteWebhookAsync(CancellationToken ct);
}

/// <summary>Результат одного цикла long-polling.</summary>
public sealed record TelegramPollResult(
    IReadOnlyList<IncomingTelegramUpdate> Updates,
    IReadOnlyList<IncomingCallbackQuery> Callbacks,
    long NextOffset);

/// <summary>Описание inline-кнопки.</summary>
public sealed record InlineButton(string Text, string CallbackData);
