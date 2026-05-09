namespace FinanceBot.Application.Actors.Telegram.Messages;

/// <summary>
/// Входящее сообщение от Telegram. Создаётся polling/webhook-сервисом, маршрутизируется в <c>TelegramGatewayActor</c>.
/// </summary>
public sealed record IncomingTelegramUpdate(
    long UpdateId,
    long ChatId,
    long TelegramId,
    string? Username,
    string? FirstName,
    string? LastName,
    string? Text,
    DateTimeOffset SentAt);

/// <summary>Внутреннее сообщение от gateway: «отправить ответ пользователю».</summary>
public sealed record OutgoingTelegramReply(
    long ChatId,
    string Text,
    bool DisableWebPagePreview = true);
