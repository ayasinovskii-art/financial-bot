namespace FinanceBot.Application.Configuration;

/// <summary>
/// Конверт для маршрутизации сообщения в конкретный шард. EntityId — ключ актора (userId.ToString()).
/// Cluster Sharding извлечёт <see cref="EntityId"/> и shard id через <see cref="UserShardMessageExtractor"/>.
/// </summary>
public sealed record ShardEnvelope(string EntityId, object Message);
