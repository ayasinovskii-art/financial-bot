using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Events.User;

namespace FinanceBot.Application.Projections;

/// <summary>
/// Проекция списка пользователей в <c>app.users</c>. Cluster Singleton.
/// Подписывается на тег <see cref="PersistenceTags.UserLifecycle"/>.
/// </summary>
public sealed class UsersListProjection(IProjectionOffsetStore offsetStore, IUsersReadModelWriter writer)
    : ProjectionBase(offsetStore)
{
    public const string Name = "users-list";

    protected override string ProjectionName => Name;

    protected override string Tag => PersistenceTags.UserLifecycle;

    protected override Task HandleAsync(object payload, CancellationToken ct) => payload switch
    {
        UserRegistered r => writer.UpsertOnRegistrationAsync(
            r.UserId, r.TelegramId, r.Timezone, r.OccurredAt, ct),
        UserSettingsUpdated s => writer.UpdateSettingsAsync(
            s.UserId, s.Key, s.NewValue, s.OccurredAt, ct),
        _ => Task.CompletedTask
    };
}

/// <summary>Marker-тип для регистрации singleton проекции UsersList в registry.</summary>
public sealed class UsersListProjectionMarker;
