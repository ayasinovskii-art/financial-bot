using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Common;

namespace FinanceBot.Application.Actors.Scheduler;

/// <summary>
/// Cluster singleton: отвечает за все per-user и system тики (evening / silence-deadline / salary / advisor / heartbeat).
/// На Stage 4 — пустой actor, отвечающий на Ping.
/// На Stage 16 — реальная имплементация поверх Akka.Scheduler с пересчётом тиков на изменения настроек.
/// </summary>
public sealed class SchedulerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log;

    public SchedulerActor()
    {
        _log = Context.GetLogger();

        Receive<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));

        ReceiveAny(msg => _log.Debug("SchedulerActor received unhandled {MessageType}", msg.GetType().Name));
    }

    public static Props CreateProps() => Props.Create<SchedulerActor>();
}
