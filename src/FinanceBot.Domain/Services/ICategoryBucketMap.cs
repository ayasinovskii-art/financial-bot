using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Services;

/// <summary>Override-таблица маппинга категорий в бакеты, заданная пользователем через /settings bucket_mapping.</summary>
public interface IBucketMappingOverrides
{
    /// <summary>null = override отсутствует, использовать default.</summary>
    Bucket? GetOverride(Category category);
}

/// <summary>
/// Маппинг категорий в бакеты бюджета (default + override).
/// </summary>
public interface ICategoryBucketMap
{
    /// <summary>Получить бакет для категории; overrides применяется поверх default.</summary>
    Bucket Map(Category category, IBucketMappingOverrides? overrides = null);
}
