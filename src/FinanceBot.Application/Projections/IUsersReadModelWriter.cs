namespace FinanceBot.Application.Projections;

/// <summary>
/// Запись в read-model app.users. Реализация — UsersReadModelWriter в Infrastructure.
/// </summary>
public interface IUsersReadModelWriter
{
    Task UpsertOnRegistrationAsync(
        Guid userId,
        long telegramId,
        string timezone,
        DateTimeOffset registeredAt,
        CancellationToken ct);

    Task UpdateSettingsAsync(
        Guid userId,
        string key,
        string? value,
        DateTimeOffset updatedAt,
        CancellationToken ct);
}
