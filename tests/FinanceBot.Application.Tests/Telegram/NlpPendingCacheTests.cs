using FinanceBot.Application.Actors.Telegram;
using Xunit;

namespace FinanceBot.Application.Tests.Telegram;

public sealed class NlpPendingCacheTests
{
    private static NlpPendingEntry MakeEntry(DateTimeOffset? createdAt = null) =>
        new(ChatId: 42L, UserId: Guid.NewGuid(), Context: null,
            ParsedResult: new NlpParseResult("expense", 100m, "Other", "test", 0.9, true),
            CreatedAt: createdAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Add_then_TryGet_returns_same_entry()
    {
        var cache = new NlpPendingCache();
        var key = Guid.NewGuid();
        var entry = MakeEntry();

        cache.Add(key, entry);

        Assert.True(cache.TryGet(key, out var found));
        Assert.Same(entry, found);
    }

    [Fact]
    public void TryRemove_returns_true_and_subsequent_TryGet_returns_false()
    {
        var cache = new NlpPendingCache();
        var key = Guid.NewGuid();
        cache.Add(key, MakeEntry());

        var removed = cache.TryRemove(key, out _);

        Assert.True(removed);
        Assert.False(cache.TryGet(key, out _));
    }

    [Fact]
    public void Add_new_entry_evicts_expired_entries()
    {
        var cache = new NlpPendingCache();
        var oldKey = Guid.NewGuid();
        cache.Add(oldKey, MakeEntry(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(11)));

        cache.Add(Guid.NewGuid(), MakeEntry());

        Assert.False(cache.TryGet(oldKey, out _));
    }
}
