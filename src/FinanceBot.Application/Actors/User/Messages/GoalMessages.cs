using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.User.Messages;

/// <summary>Запрос текущего списка целей пользователя (read-only, без персистирования).</summary>
public sealed record GetUserGoals(Guid UserId) : IUserShardMessage;

/// <summary>Ответ: список всех целей пользователя.</summary>
public sealed record UserGoalsList(IReadOnlyList<GoalState> Goals);

/// <summary>Reply: цель успешно добавлена.</summary>
public sealed record GoalAccepted(Guid GoalId);

/// <summary>Reply: цель успешно отмечена выполненной.</summary>
public sealed record GoalCompletedReply(Guid GoalId);

/// <summary>Reply: команда по целям отклонена.</summary>
public sealed record GoalRejected(string Reason);
