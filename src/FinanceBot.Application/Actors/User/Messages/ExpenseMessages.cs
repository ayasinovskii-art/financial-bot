using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.User.Messages;

/// <summary>Reply: трата зафиксирована, плюс остатки бакетов.</summary>
public sealed record ExpenseAccepted(
    Guid UserId,
    Guid ExpenseId,
    Guid PeriodId,
    decimal Amount,
    Category Category,
    Bucket Bucket,
    decimal SpentEssentials,
    decimal SpentFun,
    decimal SpentDeposit,
    decimal AllocationEssentials,
    decimal AllocationFun,
    decimal AllocationDeposit);

/// <summary>Reply: команда отклонена.</summary>
public sealed record ExpenseRejected(Guid UserId, string Reason);
