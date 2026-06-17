using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Application.Csv;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class ImportPendingCacheTests
{
    [Fact]
    public void TryGet_returns_false_for_expired_entry()
    {
        var cache = new ImportPendingCache();
        var key = Guid.NewGuid();
        var expiredEntry = new ImportPendingEntry(
            ChatId: 1000L,
            UserId: Guid.NewGuid(),
            Rows: Array.Empty<ParsedImportRow>(),
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-11));

        cache.Set(key, expiredEntry);

        var found = cache.TryGet(key, out var result);

        Assert.False(found);
        Assert.Null(result);
    }
}
