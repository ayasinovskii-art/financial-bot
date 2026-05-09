using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace FinanceBot.Application.Projections;

/// <summary>
/// Базовый actor для проекций, читающих EventsByTag и пишущих в read-model.
/// Cluster Singleton — гарантия одной активной копии на кластер.
/// </summary>
/// <remarks>
/// Контракт реализаций: переопределить <see cref="ProjectionName"/>, <see cref="Tag"/> и <see cref="HandleAsync"/>.
/// Offset хранится в IProjectionOffsetStore (app.projection_offsets) и обновляется после успешной обработки события.
/// </remarks>
public abstract class ProjectionBase : ReceiveActor
{
    private readonly IProjectionOffsetStore _offsetStore;
    private readonly ILoggingAdapter _log;
    private IKillSwitch? _killSwitch;

    protected ProjectionBase(IProjectionOffsetStore offsetStore)
    {
        _offsetStore = offsetStore;
        _log = Context.GetLogger();

        Receive<StartProjection>(_ => Start());
        Receive<EventEnvelope>(HandleEnvelope);
        Receive<ProjectionFailed>(failed =>
        {
            _log.Error(failed.Cause, "Projection {ProjectionName} stream failed; restarting.", ProjectionName);
            Self.Tell(new StartProjection());
        });
    }

    /// <summary>Имя проекции, ключ для offset store.</summary>
    protected abstract string ProjectionName { get; }

    /// <summary>Тег EventsByTag (один; для подписки на несколько — используйте Merge через manual setup).</summary>
    protected abstract string Tag { get; }

    /// <summary>Обработчик одного события из журнала. Возвращает task, который завершается при успешной записи в read-model.</summary>
    protected abstract Task HandleAsync(object payload, CancellationToken ct);

    protected override void PreStart()
    {
        Self.Tell(new StartProjection());
        base.PreStart();
    }

    protected override void PostStop()
    {
        _killSwitch?.Shutdown();
        base.PostStop();
    }

    private void Start()
    {
        var self = Self;
        Task.Run(async () =>
        {
            try
            {
                var offsetValue = await _offsetStore.LoadAsync(ProjectionName, CancellationToken.None);
                var offset = offsetValue == 0 ? Offset.NoOffset() : Offset.Sequence(offsetValue);

                var readJournal = PersistenceQuery.Get(Context.System)
                    .ReadJournalFor<IEventsByTagQuery>("akka.persistence.query.journal.postgresql");

                var (kill, completion) = readJournal.EventsByTag(Tag, offset)
                    .ViaMaterialized(KillSwitches.Single<EventEnvelope>(), Keep.Right)
                    .ToMaterialized(Sink.ActorRefWithAck<EventEnvelope>(
                        self,
                        onInitMessage: new ProjectionInit(),
                        ackMessage: new ProjectionAck(),
                        onCompleteMessage: new ProjectionComplete(),
                        onFailureMessage: ex => new ProjectionFailed(ex)), Keep.Both)
                    .Run(Context.Materializer());

                _killSwitch = kill;
                _ = completion;
            }
            catch (Exception ex)
            {
                self.Tell(new ProjectionFailed(ex));
            }
        });
    }

    private void HandleEnvelope(EventEnvelope envelope)
    {
        var sender = Sender;
        var self = Self;
        var name = ProjectionName;
        var offset = (envelope.Offset as Sequence)?.Value ?? 0L;

        Task.Run(async () =>
        {
            try
            {
                await HandleAsync(envelope.Event, CancellationToken.None);
                if (offset > 0)
                {
                    await _offsetStore.SaveAsync(name, offset, CancellationToken.None);
                }
                sender.Tell(new ProjectionAck());
            }
            catch (Exception ex)
            {
                self.Tell(new ProjectionFailed(ex));
            }
        });
    }

    /// <summary>Команда: запустить (или перезапустить) подписку на journal.</summary>
    public sealed record StartProjection;

    /// <summary>Init-сообщение от Akka Streams.</summary>
    public sealed record ProjectionInit;

    /// <summary>Ack-сообщение для back-pressure.</summary>
    public sealed record ProjectionAck;

    /// <summary>Complete-сообщение от Akka Streams.</summary>
    public sealed record ProjectionComplete;

    /// <summary>Сообщение об ошибке стрима — приводит к рестарту подписки.</summary>
    public sealed record ProjectionFailed(Exception Cause);
}
