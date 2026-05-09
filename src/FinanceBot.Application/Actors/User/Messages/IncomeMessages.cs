namespace FinanceBot.Application.Actors.User.Messages;

/// <summary>Reply: доход успешно зафиксирован, плюс актуальное состояние периода.</summary>
public sealed record IncomeAccepted(
    Guid UserId,
    Guid IncomeId,
    Guid PeriodId,
    DateOnly PeriodStartDate,
    decimal TotalIncome,
    decimal AllocationEssentials,
    decimal AllocationFun,
    decimal AllocationDeposit);

/// <summary>Reply: команда отклонена с сообщением для пользователя.</summary>
public sealed record IncomeRejected(Guid UserId, string Reason);
