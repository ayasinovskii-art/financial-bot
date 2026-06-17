using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Services;

/// <summary>Результат распознавания выписки: список транзакций либо причина провала.</summary>
public sealed record StatementExtractionResult(
    bool IsSuccess,
    IReadOnlyList<ImportedTransaction> Transactions,
    string? FailureMessage,
    ClaudeUnavailabilityReason? FailureReason)
{
    public static StatementExtractionResult Success(IReadOnlyList<ImportedTransaction> transactions)
        => new(true, transactions, null, null);

    public static StatementExtractionResult Failure(ClaudeUnavailabilityReason reason, string message)
        => new(false, Array.Empty<ImportedTransaction>(), message, reason);
}

/// <summary>
/// Распознаёт транзакции из изображения выписки (Claude Vision). Реализация —
/// ClaudeStatementExtractor в Infrastructure поверх <see cref="IClaudeClient"/>.
/// </summary>
public interface IStatementExtractor
{
    Task<StatementExtractionResult> ExtractAsync(ReadOnlyMemory<byte> image, string mediaType, CancellationToken ct);
}
