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

    /// <summary>Получить пачку обновлений (long-polling). Возвращает offset следующего вызова.</summary>
    Task<TelegramPollResult> PollAsync(long offset, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Результат одного цикла long-polling.</summary>
public sealed record TelegramPollResult(IReadOnlyList<IncomingTelegramUpdate> Updates, long NextOffset);
