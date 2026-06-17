namespace FinanceBot.Domain.ValueObjects;

public sealed record BulkExpenseRow(
    decimal Amount,
    DateOnly Date,
    string Description);
