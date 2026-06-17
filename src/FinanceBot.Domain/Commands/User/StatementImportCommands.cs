using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Domain.Commands.User;

/// <summary>
/// Предложить пользователю импорт распознанных транзакций. UserActor сохраняет их как
/// transient pending-proposal и ждёт подтверждения (<see cref="ConfirmStatementImport"/>).
/// </summary>
public sealed record ProposeStatementImport(
    Guid UserId,
    Guid ProposalId,
    IReadOnlyList<ImportedTransaction> Transactions) : IUserScopedCommand;

/// <summary>Подтвердить ранее предложенный импорт (по ProposalId). Массово фиксирует транзакции.</summary>
public sealed record ConfirmStatementImport(
    Guid UserId,
    Guid ProposalId) : IUserScopedCommand;

/// <summary>Отменить ожидающее предложение импорта.</summary>
public sealed record CancelStatementImport(Guid UserId) : IUserScopedCommand;
