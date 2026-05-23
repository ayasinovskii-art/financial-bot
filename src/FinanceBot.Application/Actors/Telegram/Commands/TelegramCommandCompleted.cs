namespace FinanceBot.Application.Actors.Telegram.Commands;

/// <summary>
/// Универсальный envelope для возврата результата Ask'а в actor-thread.
/// Заменяет 12 ранее существовавших <c>XReplyResult</c> records.
/// Реализация <paramref name="Outgoing"/> вычисляется в <c>PipeTo</c>-callback'е,
/// поэтому исключение/успех уже сериализованы в готовый список outgoing-сообщений.
/// </summary>
internal sealed record TelegramCommandCompleted(IEnumerable<object> Outgoing);
