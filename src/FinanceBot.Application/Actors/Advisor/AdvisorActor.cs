using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Domain.Events.Advisor;

namespace FinanceBot.Application.Actors.Advisor;

/// <summary>
/// Per-node actor (зарегистрирован маркером в registry). Строит AdvisorSnapshot из read-model
/// и формирует локальный совет на эвристиках при недоступности Claude.
/// </summary>
public sealed class AdvisorActor : ReceiveActor
{
    private readonly IAdvisorSnapshotReader _reader;
    private readonly ILoggingAdapter _log;

    public AdvisorActor(IAdvisorSnapshotReader reader)
    {
        _reader = reader;
        _log = Context.GetLogger();

        Receive<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));
        Receive<BuildSnapshotRequest>(HandleBuildSnapshot);
        Receive<BuildLocalAdviceRequest>(HandleBuildLocal);
    }

    private void HandleBuildSnapshot(BuildSnapshotRequest req)
    {
        var sender = Sender;
        var reader = _reader;
        var log = _log;
        Task.Run(async () =>
        {
            try
            {
                var snap = await reader.BuildAsync(req.UserId, DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false);
                sender.Tell(new BuildSnapshotResponse(req.CorrelationId, req.UserId, snap, ErrorMessage: null));
            }
            catch (Exception ex)
            {
                log.Warning(ex, "AdvisorActor: snapshot build failed for {UserId}.", req.UserId);
                sender.Tell(new BuildSnapshotResponse(req.CorrelationId, req.UserId, Snapshot: null, ErrorMessage: ex.Message));
            }
        });
    }

    private void HandleBuildLocal(BuildLocalAdviceRequest req)
    {
        var text = LocalAdviceBuilder.Build(req.Snapshot, req.TickType);
        Sender.Tell(new BuildLocalAdviceResponse(req.CorrelationId, req.UserId, text));
    }

    public static Props CreateProps(IAdvisorSnapshotReader reader) =>
        Props.Create(() => new AdvisorActor(reader));
}

/// <summary>Marker для регистрации в ActorRegistry.</summary>
public sealed class AdvisorActorMarker;

public sealed record BuildSnapshotRequest(Guid CorrelationId, Guid UserId);
public sealed record BuildSnapshotResponse(Guid CorrelationId, Guid UserId, AdvisorSnapshot? Snapshot, string? ErrorMessage);

public sealed record BuildLocalAdviceRequest(Guid CorrelationId, Guid UserId, AdvisorSnapshot Snapshot, AdvisorTickType TickType);
public sealed record BuildLocalAdviceResponse(Guid CorrelationId, Guid UserId, string Text);
