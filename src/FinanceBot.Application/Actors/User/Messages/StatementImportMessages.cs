using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.User.Messages;

/// <summary>Query: вернуть транзакции ожидающего предложения импорта (для кнопки «Список»).</summary>
public sealed record GetPendingStatementImport(Guid UserId);

/// <summary>Reply: импорт предложен, ждём подтверждения (по ProposalId).</summary>
public sealed record StatementImportProposed(
    Guid ProposalId,
    int Count,
    int ExpenseCount,
    int IncomeCount,
    decimal ExpenseTotal,
    decimal IncomeTotal);

/// <summary>Reply: импорт выполнен.</summary>
public sealed record StatementImportCompleted(
    int Imported,
    int SkippedDuplicates,
    int Failed,
    decimal ExpenseTotal,
    decimal IncomeTotal);

/// <summary>Reply: импорт отклонён или предложение неактуально.</summary>
public sealed record StatementImportRejected(string Reason);

/// <summary>Reply: импорт отменён пользователем.</summary>
public sealed record StatementImportCancelled;

/// <summary>Reply: список транзакций ожидающего импорта (пусто, если предложения нет).</summary>
public sealed record StatementImportList(
    Guid ProposalId,
    IReadOnlyList<ImportedTransaction> Transactions);
