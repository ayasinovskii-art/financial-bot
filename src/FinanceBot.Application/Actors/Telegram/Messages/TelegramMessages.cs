namespace FinanceBot.Application.Actors.Telegram.Messages;

/// <summary>
/// Входящее сообщение от Telegram. Создаётся polling/webhook-сервисом, маршрутизируется в <c>TelegramGatewayActor</c>.
/// CorrelationId — генерируется при приёме (для трассировки в логах).
/// </summary>
public sealed record IncomingTelegramUpdate(
    long UpdateId,
    long ChatId,
    long TelegramId,
    string? Username,
    string? FirstName,
    string? LastName,
    string? Text,
    DateTimeOffset SentAt)
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
}

/// <summary>Внутреннее сообщение от gateway: «отправить ответ пользователю».</summary>
public sealed record OutgoingTelegramReply(
    long ChatId,
    string Text,
    bool DisableWebPagePreview = true);

/// <summary>Inline-сообщение бота с кнопками.</summary>
public sealed record OutgoingInlineKeyboard(
    long ChatId,
    string Text,
    IReadOnlyList<IReadOnlyList<FinanceBot.Application.Telegram.InlineButton>> Rows);

/// <summary>Подтверждение нажатия inline-кнопки.</summary>
public sealed record OutgoingCallbackAck(string CallbackQueryId, string? Text);

/// <summary>Отправить фото (PNG) с подписью пользователю.</summary>
public sealed record OutgoingTelegramPhoto(
    long ChatId,
    byte[] Photo,
    string FileName,
    string? Caption);

/// <summary>Отправить документ (файл) с подписью пользователю.</summary>
public sealed record OutgoingTelegramDocument(
    long ChatId,
    byte[] Document,
    string FileName,
    string? Caption);

/// <summary>Входящий callback от inline-кнопки.</summary>
public sealed record IncomingCallbackQuery(
    long UpdateId,
    string CallbackQueryId,
    long ChatId,
    long TelegramId,
    string? Username,
    string? FirstName,
    string Data,
    DateTimeOffset SentAt)
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
}
