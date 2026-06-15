namespace FinanceBot.Application.Configuration;

/// <summary>Канонические теги, навешиваемые на доменные события для подписки проекций.</summary>
public static class PersistenceTags
{
    public const string Expense = "expense";
    public const string Income = "income";
    public const string Period = "period";
    public const string Category = "category";
    public const string Whitelist = "whitelist";
    public const string Settings = "settings";
    public const string Advisor = "advisor";
    public const string Report = "report";

    public const string Goal = "goal";

    public const string Notification = "notification";

    /// <summary>Тег жизненного цикла пользователя: UserRegistered + UserSettingsUpdated. Используется UsersListProjection.</summary>
    public const string UserLifecycle = "user-lifecycle";

    /// <summary>Префикс per-user тега. Полный тег: <c>user-{userId}</c>.</summary>
    public const string UserPrefix = "user-";

    public static string ForUser(Guid userId) => UserPrefix + userId.ToString("N");
}
