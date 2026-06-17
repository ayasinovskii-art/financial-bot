namespace FinanceBot.Application.Actors.User.Messages;

/// <summary>Reply: операция удаления (трата/доход/цель) выполнена успешно.</summary>
public sealed record DeletedSuccessfully;

/// <summary>Reply: операция удаления отклонена.</summary>
public sealed record DeleteRejected(string Reason);
