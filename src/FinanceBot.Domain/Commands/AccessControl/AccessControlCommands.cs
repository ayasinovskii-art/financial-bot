namespace FinanceBot.Domain.Commands.AccessControl;

public sealed record WhitelistUser(
    long AdminId,
    long TelegramId) : ICommand;

public sealed record RevokeUser(
    long AdminId,
    long TelegramId) : ICommand;

public sealed record ListWhitelisted : ICommand;

/// <summary>Запрос проверки доступа: разрешён ли указанный telegramId.</summary>
public sealed record IsAllowed(long TelegramId) : ICommand;
