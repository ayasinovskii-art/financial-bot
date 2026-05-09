using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Application.Projections;

namespace FinanceBot.Application.Actors.Scheduler;

/// <summary>
/// Cluster singleton: записывает SystemHeartbeat каждую минуту и хост-агрегирует
/// per-user тики (Stage 16+ — расширяется по мере добавления EveningTick / SalaryDayTick / …).
/// </summary>
public sealed class SchedulerActor : ReceiveActor, IWithTimers
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(1);
    private const string HeartbeatKey = "system-heartbeat";

    private readonly ISystemHeartbeatWriter _heartbeatWriter;
    private readonly ILoggingAdapter _log;

    public ITimerScheduler Timers { get; set; } = null!;

    public SchedulerActor(ISystemHeartbeatWriter heartbeatWriter)
    {
        _heartbeatWriter = heartbeatWriter;
        _log = Context.GetLogger();

        Receive<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));
        Receive<HeartbeatTick>(OnHeartbeat);

        ReceiveAny(msg => _log.Debug("SchedulerActor received unhandled {MessageType}", msg.GetType().Name));
    }

    protected override void PreStart()
    {
        // Первый тик через 5 секунд (даём другим компонентам подняться), потом каждую минуту.
        Timers.StartPeriodicTimer(
            key: HeartbeatKey,
            msg: new HeartbeatTick(),
            initialDelay: TimeSpan.FromSeconds(5),
            interval: HeartbeatInterval);
        base.PreStart();
    }

    private void OnHeartbeat(HeartbeatTick tick)
    {
        _ = tick;
        _ = WriteHeartbeatAsync();
    }

    private async Task WriteHeartbeatAsync()
    {
        try
        {
            await _heartbeatWriter.UpsertAsync(DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to write system heartbeat.");
        }
    }

    public static Props CreateProps(ISystemHeartbeatWriter writer)
        => Props.Create(() => new SchedulerActor(writer));

    private sealed record HeartbeatTick;
}
