namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Бакет бюджета. Категория трат маппится в один бакет через ICategoryBucketMap.
/// </summary>
public enum Bucket
{
    /// <summary>Не привязано к бакету (например, для типа Deposit на уровне категорий).</summary>
    None = 0,
    Essentials = 1,
    Fun = 2,
    Deposit = 3
}
