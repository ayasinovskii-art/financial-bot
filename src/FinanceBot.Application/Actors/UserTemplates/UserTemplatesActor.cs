using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using FinanceBot.Application.Actors.Common;

namespace FinanceBot.Application.Actors.UserTemplates;

/// <summary>
/// Per-user persistent actor для регулярных шаблонов трат. PersistenceId = "user-templates-{userId}".
/// Stage 4 — только skeleton. Реализация — Stage 13.
/// </summary>
public sealed class UserTemplatesActor : ReceivePersistentActor
{
    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;

    public override string PersistenceId { get; }

    public UserTemplatesActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();
        PersistenceId = $"user-templates-{userId:N}";

        Command<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));
        CommandAny(msg => _log.Debug("UserTemplatesActor[{UserId}] received unhandled {MessageType}", _userId, msg.GetType().Name));
    }

    public static Props CreateProps(Guid userId) => Props.Create(() => new UserTemplatesActor(userId));

    public static Props CreatePropsFromEntityId(string entityId)
        => CreateProps(Guid.ParseExact(entityId, "N"));
}
