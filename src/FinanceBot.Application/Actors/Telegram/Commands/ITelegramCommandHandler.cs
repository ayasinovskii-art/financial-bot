using FinanceBot.Application.Actors.Telegram.Messages;

namespace FinanceBot.Application.Actors.Telegram.Commands;

/// <summary>
/// Strategy для одной Telegram-команды. Регистрируется в <see cref="TelegramGatewayActor"/>
/// и активируется диспетчером по <see cref="TelegramCommandKind"/>.
/// </summary>
public interface ITelegramCommandHandler
{
    /// <summary>Команда, на которую реагирует handler.</summary>
    TelegramCommandKind Kind { get; }

    /// <summary>Выполнить команду в контексте конкретного апдейта.</summary>
    void Execute(TelegramCommandContext ctx);
}

/// <summary>
/// Strategy для inline-callback'ов (кнопки в сообщениях бота).
/// Регистрируются с уникальным префиксом callback data; диспетчер выбирает по префиксу.
/// </summary>
public interface ITelegramCallbackHandler
{
    /// <summary>Префикс <c>callback_data</c> (например, <c>"correct:"</c>).</summary>
    string DataPrefix { get; }

    void Execute(TelegramCallbackContext ctx);
}
