using System.Text.Json;
using FinanceBot.Application.Scheduling;
using FinanceBot.Domain.ValueObjects;
using FinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Scheduling;

/// <summary>
/// Реализация <see cref="IUserDirectory"/> поверх read-model app.users.
/// Десериализует settings_json в UserScheduleSettings.
/// </summary>
public sealed class UserDirectoryReader(IDbContextFactory<AppDbContext> dbFactory) : IUserDirectory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<UserDirectoryEntry>> ListAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await db.Users
            .AsNoTracking()
            .Select(u => new { u.UserId, u.TelegramId, u.Timezone, u.SettingsJson })
            .ToListAsync(ct);

        var result = new List<UserDirectoryEntry>(rows.Count);
        foreach (var r in rows)
        {
            TimeZoneInfo defaultTz;
            try { defaultTz = TimeZoneInfo.FindSystemTimeZoneById(r.Timezone); }
            catch (TimeZoneNotFoundException) { defaultTz = TimeZoneInfo.Utc; }
            catch (InvalidTimeZoneException) { defaultTz = TimeZoneInfo.Utc; }

            var dict = ParseSettingsJson(r.SettingsJson);
            var settings = UserScheduleSettings.FromDictionary(dict, defaultTz);
            result.Add(new UserDirectoryEntry(r.UserId, r.TelegramId, settings));
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string?> ParseSettingsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetRawText()
                };
            }
            return dict;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }
}
