using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Services;

/// <summary>
/// Default-маппинг категорий в бакеты согласно ТЗ §3.4. Stateless.
/// </summary>
public sealed class DefaultCategoryBucketMap : ICategoryBucketMap
{
    public Bucket Map(Category category, IBucketMappingOverrides? overrides = null)
    {
        if (overrides?.GetOverride(category) is { } overridden)
        {
            return overridden;
        }

        return category switch
        {
            Category.Groceries => Bucket.Essentials,
            Category.Transport => Bucket.Essentials,
            Category.Utilities => Bucket.Essentials,
            Category.Health => Bucket.Essentials,
            Category.Education => Bucket.Essentials,
            Category.Other => Bucket.Essentials,
            Category.DiningOut => Bucket.Fun,
            Category.Subscriptions => Bucket.Fun,
            Category.Entertainment => Bucket.Fun,
            Category.Personal => Bucket.Fun,
            Category.Clothing => Bucket.Fun,
            Category.Gifts => Bucket.Fun,
            Category.Travel => Bucket.Fun,
            _ => Bucket.Essentials
        };
    }
}
