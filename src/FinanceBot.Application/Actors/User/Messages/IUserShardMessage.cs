namespace FinanceBot.Application.Actors.User.Messages;

/// <summary>
/// Маркер сообщений, которые маршрутизируются в shard region User.
/// Все такие сообщения должны иметь UserId — это ключ entityId.
/// </summary>
public interface IUserShardMessage
{
    Guid UserId { get; }
}
