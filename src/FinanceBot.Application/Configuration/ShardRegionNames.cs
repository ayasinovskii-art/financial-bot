namespace FinanceBot.Application.Configuration;

/// <summary>Канонические имена shard regions, используются для DI lookup и в HOCON-теге роли.</summary>
public static class ShardRegionNames
{
    public const string User = "user";
    public const string UserTemplates = "user-templates";
    public const string UserPlannedExpenses = "user-planned-expenses";
}
