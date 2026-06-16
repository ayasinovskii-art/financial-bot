using System.Collections.Concurrent;
using FinanceBot.Application.Actors.Telegram.Commands;

namespace FinanceBot.Application.Actors.Telegram;

public sealed record NlpPendingEntry(
    long ChatId,
    Guid UserId,
    TelegramCommandContext? Context,
    NlpParseResult? ParsedResult,
    DateTimeOffset CreatedAt);

/// <summary>
/// Thread-safe in-memory store for pending NLP parse operations.
/// Entries older than 10 minutes are evicted lazily on each Add.
/// </summary>
public sealed class NlpPendingCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<Guid, NlpPendingEntry> _cache = new();

    public void Add(Guid key, NlpPendingEntry entry)
    {
        EvictExpired();
        _cache[key] = entry;
    }

    public bool TryGet(Guid key, out NlpPendingEntry? entry) =>
        _cache.TryGetValue(key, out entry);

    public bool TryRemove(Guid key, out NlpPendingEntry? entry) =>
        _cache.TryRemove(key, out entry);

    private void EvictExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - Ttl;
        foreach (var key in _cache.Keys)
        {
            if (_cache.TryGetValue(key, out var e) && e.CreatedAt < cutoff)
                _cache.TryRemove(key, out _);
        }
    }
}
