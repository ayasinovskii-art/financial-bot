using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Scheduling;

/// <summary>
/// Чтение списка зарегистрированных пользователей и их настроек из read-model (app.users).
/// Используется SchedulerActor для перебора per-user тиков.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Снимок всех пользователей: userId + распарсенные расписание-релевантные настройки.</summary>
    Task<IReadOnlyList<UserDirectoryEntry>> ListAsync(CancellationToken ct);
}

public sealed record UserDirectoryEntry(Guid UserId, long TelegramId, UserScheduleSettings Settings);
