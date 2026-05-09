using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.User.Messages;

/// <summary>Запрос текущего среза состояния пользователя (только для read-команд бота).</summary>
public sealed record GetUserSnapshot(Guid UserId) : IUserShardMessage;

/// <summary>Snapshot ответ.</summary>
public sealed record UserSnapshot(
    Guid UserId,
    bool IsRegistered,
    long? TelegramId,
    string? Timezone,
    IReadOnlyDictionary<string, string?> Settings);

/// <summary>Reply: настройка успешно изменена.</summary>
public sealed record SettingsUpdated(Guid UserId, SettingsKey Key, string? OldValue, string? NewValue);

/// <summary>Reply: значение настройки невалидно.</summary>
public sealed record SettingsValidationFailed(Guid UserId, SettingsKey Key, string Reason);

/// <summary>Reply: настройка(и) сброшена(ы).</summary>
public sealed record SettingsResetCompleted(Guid UserId, SettingsKey? Key);
