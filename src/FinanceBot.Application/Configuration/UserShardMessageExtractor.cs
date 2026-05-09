using Akka.Cluster.Sharding;

namespace FinanceBot.Application.Configuration;

/// <summary>
/// MessageExtractor для всех "user-scoped" shard regions (User, UserTemplates, UserPlannedExpenses).
/// Все три region'а используют одну и ту же стратегию: ключ актора = userId.ToString(), shard id = stableHash(entityId) mod shardCount.
/// </summary>
public sealed class UserShardMessageExtractor(int maxShards) : HashCodeMessageExtractor(maxShards)
{
    public override string? EntityId(object message) => message switch
    {
        ShardEnvelope env => env.EntityId,
        _ => null
    };

    public override object EntityMessage(object message) => message switch
    {
        ShardEnvelope env => env.Message,
        _ => message
    };
}
