namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Фиксированный список категорий трат.
/// Расширение списка — breaking change, требует версионирования событий.
/// </summary>
public enum Category
{
    Groceries = 1,
    DiningOut = 2,
    Transport = 3,
    Utilities = 4,
    Subscriptions = 5,
    Entertainment = 6,
    Health = 7,
    Clothing = 8,
    Personal = 9,
    Education = 10,
    Gifts = 11,
    Travel = 12,
    Other = 99
}

public static class CategoryExtensions
{
    public static IReadOnlyCollection<Category> All { get; } =
        Enum.GetValues<Category>();

    public static bool TryParse(string? value, out Category category)
        => Enum.TryParse(value, ignoreCase: true, out category)
           && Enum.IsDefined(category);
}
