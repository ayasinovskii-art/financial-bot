using System.Collections.Concurrent;
using FinanceBot.Application.Csv;

namespace FinanceBot.Application.Actors.Telegram;

public sealed record ImportPendingEntry(
    long ChatId,
    Guid UserId,
    IReadOnlyList<ParsedImportRow> Rows,
    DateTimeOffset CreatedAt);

public sealed class ImportPendingCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<Guid, ImportPendingEntry> _cache = new();

    public void Set(Guid key, ImportPendingEntry entry)
    {
        EvictExpired();
        _cache[key] = entry;
    }

    public bool TryGet(Guid key, out ImportPendingEntry? entry)
    {
        if (!_cache.TryGetValue(key, out entry))
            return false;
        if (DateTimeOffset.UtcNow > entry!.CreatedAt + Ttl)
        {
            _cache.TryRemove(key, out _);
            entry = null;
            return false;
        }
        return true;
    }

    public bool TryRemove(Guid key, out ImportPendingEntry? entry) =>
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
