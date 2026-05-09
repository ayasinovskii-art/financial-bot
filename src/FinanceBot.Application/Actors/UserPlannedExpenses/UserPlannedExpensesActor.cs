using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using FinanceBot.Application.Actors.Common;

namespace FinanceBot.Application.Actors.UserPlannedExpenses;

/// <summary>
/// Per-user persistent actor для запланированных трат. PersistenceId = "user-planned-{userId}".
/// Stage 4 — только skeleton. Реализация — Stage 14.
/// </summary>
public sealed class UserPlannedExpensesActor : ReceivePersistentActor
{
    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;

    public override string PersistenceId { get; }

    public UserPlannedExpensesActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();
        PersistenceId = $"user-planned-{userId:N}";

        Command<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));
        CommandAny(msg => _log.Debug("UserPlannedExpensesActor[{UserId}] received unhandled {MessageType}", _userId, msg.GetType().Name));
    }

    public static Props CreateProps(Guid userId) => Props.Create(() => new UserPlannedExpensesActor(userId));

    public static Props CreatePropsFromEntityId(string entityId)
        => CreateProps(Guid.ParseExact(entityId, "N"));
}
