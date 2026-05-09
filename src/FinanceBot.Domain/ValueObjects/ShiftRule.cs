namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Правило сдвига дня зарплаты при попадании на выходной/праздник.
/// </summary>
public enum ShiftRule
{
    /// <summary>Сдвинуть на предыдущий рабочий день.</summary>
    Previous = 1,
    /// <summary>Сдвинуть на следующий рабочий день.</summary>
    Next = 2,
    /// <summary>Не сдвигать (использовать исходную дату как есть).</summary>
    None = 3
}
