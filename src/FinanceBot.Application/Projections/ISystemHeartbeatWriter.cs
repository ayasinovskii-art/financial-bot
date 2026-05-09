namespace FinanceBot.Application.Projections;

public interface ISystemHeartbeatWriter
{
    Task UpsertAsync(DateTimeOffset lastSeen, CancellationToken ct);
    Task<DateTimeOffset?> ReadLastSeenAsync(CancellationToken ct);
}
