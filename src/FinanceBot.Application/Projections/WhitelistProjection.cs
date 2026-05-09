using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Events.Whitelist;

namespace FinanceBot.Application.Projections;

/// <summary>Проекция тегa whitelist в app.whitelist. Cluster Singleton.</summary>
public sealed class WhitelistProjection(IProjectionOffsetStore offsetStore, IWhitelistReadModelWriter writer)
    : ProjectionBase(offsetStore)
{
    public const string Name = "whitelist";

    protected override string ProjectionName => Name;
    protected override string Tag => PersistenceTags.Whitelist;

    protected override Task HandleAsync(object payload, CancellationToken ct) => payload switch
    {
        UserWhitelisted w => writer.UpsertWhitelistedAsync(w.TelegramId, w.AdminId, w.OccurredAt, ct),
        UserRevoked r => writer.MarkRevokedAsync(r.TelegramId, r.OccurredAt, ct),
        _ => Task.CompletedTask
    };
}

public sealed class WhitelistProjectionMarker;
