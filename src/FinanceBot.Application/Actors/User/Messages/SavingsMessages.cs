namespace FinanceBot.Application.Actors.User.Messages;

/// <summary>Reply: фактический перевод на накопления зафиксирован.</summary>
public sealed record SavingsAccepted(Guid UserId, Guid PeriodId, decimal Amount);

/// <summary>Reply: команда /savings отклонена.</summary>
public sealed record SavingsRejected(Guid UserId, string Reason);
