namespace FinanceBot.Infrastructure.Persistence.Entities;

/// <summary>
/// Single-row таблица для отслеживания живости. Обновляется SchedulerActor каждую минуту.
/// При gap > 5 минут — детект простоя, шлём wakeup-уведомления.
/// </summary>
public sealed class SystemHeartbeatEntity
{
    public int Id { get; set; } = 1;
    public DateTimeOffset LastSeen { get; set; }
}
