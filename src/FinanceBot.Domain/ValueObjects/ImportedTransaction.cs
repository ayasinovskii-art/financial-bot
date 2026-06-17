namespace FinanceBot.Domain.ValueObjects;

/// <summary>Тип распознанной из выписки транзакции.</summary>
public enum TransactionKind
{
    Expense = 1,
    Income = 2
}

/// <summary>
/// Одна транзакция, распознанная из банковской выписки (скриншот или CSV) перед импортом.
/// Категория НЕ хранится — назначается на этапе импорта обычным путём категоризации.
/// </summary>
public sealed record ImportedTransaction(
    DateOnly Date,
    decimal Amount,
    string Description,
    TransactionKind Kind);
