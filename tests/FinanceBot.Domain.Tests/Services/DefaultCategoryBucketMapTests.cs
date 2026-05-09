using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Domain.Tests.Services;

public sealed class DefaultCategoryBucketMapTests
{
    private readonly DefaultCategoryBucketMap _map = new();

    [Theory]
    [InlineData(Category.Groceries, Bucket.Essentials)]
    [InlineData(Category.Transport, Bucket.Essentials)]
    [InlineData(Category.Utilities, Bucket.Essentials)]
    [InlineData(Category.Health, Bucket.Essentials)]
    [InlineData(Category.Education, Bucket.Essentials)]
    [InlineData(Category.Other, Bucket.Essentials)]
    [InlineData(Category.DiningOut, Bucket.Fun)]
    [InlineData(Category.Subscriptions, Bucket.Fun)]
    [InlineData(Category.Entertainment, Bucket.Fun)]
    [InlineData(Category.Personal, Bucket.Fun)]
    [InlineData(Category.Clothing, Bucket.Fun)]
    [InlineData(Category.Gifts, Bucket.Fun)]
    [InlineData(Category.Travel, Bucket.Fun)]
    public void Default_mapping_per_spec(Category category, Bucket bucket)
    {
        _map.Map(category).Should().Be(bucket);
    }

    [Fact]
    public void Override_takes_precedence_over_default()
    {
        var overrides = new StubOverrides(Category.Groceries, Bucket.Fun);
        _map.Map(Category.Groceries, overrides).Should().Be(Bucket.Fun);
    }

    private sealed class StubOverrides(Category category, Bucket bucket) : IBucketMappingOverrides
    {
        public Bucket? GetOverride(Category c) => c == category ? bucket : null;
    }
}
