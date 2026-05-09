using System.Text.Json;
using FinanceBot.Application.Projections;
using FinanceBot.Infrastructure.Persistence;
using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Projections;

/// <summary>
/// Запись/обновление строк в <c>app.users</c>. Идемпотентность гарантируется upsert-логикой.
/// </summary>
public sealed class UsersReadModelWriter(IDbContextFactory<AppDbContext> dbFactory) : IUsersReadModelWriter
{
    public async Task UpsertOnRegistrationAsync(
        Guid userId,
        long telegramId,
        string timezone,
        DateTimeOffset registeredAt,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existing = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (existing is null)
        {
            db.Users.Add(new UserEntity
            {
                UserId = userId,
                TelegramId = telegramId,
                Timezone = timezone,
                SettingsJson = "{}",
                RegisteredAt = registeredAt,
                LastUpdated = registeredAt
            });
        }
        else
        {
            existing.TelegramId = telegramId;
            existing.Timezone = timezone;
            existing.LastUpdated = registeredAt;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateSettingsAsync(
        Guid userId,
        string key,
        string? value,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null)
        {
            return;
        }

        var settings = string.IsNullOrEmpty(user.SettingsJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(user.SettingsJson)
              ?? new Dictionary<string, JsonElement>();

        if (value is null)
        {
            settings.Remove(key);
        }
        else
        {
            settings[key] = JsonDocument.Parse($"\"{value.Replace("\"", "\\\"")}\"").RootElement;
        }

        user.SettingsJson = JsonSerializer.Serialize(settings);
        user.LastUpdated = updatedAt;

        await db.SaveChangesAsync(ct);
    }
}
