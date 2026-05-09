using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Services;

/// <summary>
/// Локальные правила категоризации (keyword/regex). Реализация — JsonCategoryRules.
/// </summary>
public interface ICategoryRules
{
    /// <summary>Найти категорию по нормализованному описанию. null = не сматчилось.</summary>
    Category? Match(NormalizedDescription description);
}
