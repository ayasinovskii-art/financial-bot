using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Application.Actors.UserPlannedExpenses.Messages;
using FinanceBot.Domain.Commands.Planned;
using FinanceBot.Domain.Events.Planned;

namespace FinanceBot.Application.Actors.UserPlannedExpenses;

/// <summary>
/// Per-user persistent actor для запланированных трат. PersistenceId = "user-planned-{userId:N}".
/// </summary>
public sealed class UserPlannedExpensesActor : ReceivePersistentActor
{
    private const int SnapshotEvery = 100;

    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;
    private PlansState _state = PlansState.Empty;
    private long _eventsSinceSnapshot;

    public override string PersistenceId { get; }

    public UserPlannedExpensesActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();
        PersistenceId = $"user-planned-{userId:N}";

        Recover<PlannedExpenseAdded>(ApplyEvent);
        Recover<PlannedExpenseConfirmed>(ApplyEvent);
        Recover<PlannedExpenseCancelled>(ApplyEvent);
        Recover<SnapshotOffer>(offer =>
        {
            if (offer.Snapshot is PlansState snap)
            {
                _state = snap;
            }
        });

        Command<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));
        Command<AddPlanned>(HandleAdd);
        Command<RemovePlanned>(HandleRemove);
        Command<ConfirmPlanned>(HandleConfirm);
        Command<ListPlanned>(_ => Sender.Tell(new PlannedList(_userId, _state.AsViews())));

        Command<SaveSnapshotSuccess>(_ => { });
        Command<SaveSnapshotFailure>(failure => _log.Error(failure.Cause, "Plans snapshot save failed."));

        CommandAny(msg => _log.Debug("UserPlannedExpensesActor[{UserId}] received unhandled {MessageType}", _userId, msg.GetType().Name));
    }

    private void HandleAdd(AddPlanned cmd)
    {
        if (cmd.Amount <= 0m)
        {
            Sender.Tell(new PlannedRejected(_userId, "Сумма должна быть положительной."));
            return;
        }
        if (string.IsNullOrWhiteSpace(cmd.Description))
        {
            Sender.Tell(new PlannedRejected(_userId, "Описание обязательно."));
            return;
        }

        var plannedId = Guid.NewGuid();
        var evt = new PlannedExpenseAdded(_userId, plannedId, cmd.Amount, cmd.Date, cmd.Description, DateTimeOffset.UtcNow);
        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            sender.Tell(new PlannedAdded(_userId,
                new PlannedExpenseView(plannedId, cmd.Amount, cmd.Date, cmd.Description, PlannedStatus.Active, null)));
        });
    }

    private void HandleRemove(RemovePlanned cmd)
    {
        if (!_state.ById.TryGetValue(cmd.PlannedId, out var plan) || plan.Status != PlannedStatus.Active)
        {
            Sender.Tell(new PlannedRejected(_userId, "Запланированная трата не найдена или уже подтверждена/отменена."));
            return;
        }

        var evt = new PlannedExpenseCancelled(_userId, cmd.PlannedId, DateTimeOffset.UtcNow);
        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            sender.Tell(new PlannedRemoved(_userId, persisted.PlannedId));
        });
    }

    private void HandleConfirm(ConfirmPlanned cmd)
    {
        if (!_state.ById.TryGetValue(cmd.PlannedId, out var plan) || plan.Status != PlannedStatus.Active)
        {
            Sender.Tell(new PlannedRejected(_userId, "Запланированная трата не найдена или уже подтверждена/отменена."));
            return;
        }

        var actual = cmd.ActualAmount ?? plan.Amount;
        var expenseId = Guid.NewGuid();
        var evt = new PlannedExpenseConfirmed(_userId, cmd.PlannedId, expenseId, actual, DateTimeOffset.UtcNow);
        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            sender.Tell(new PlannedConfirmed(_userId, persisted.PlannedId, persisted.ExpenseId));
        });
    }

    private void ApplyEvent(PlannedExpenseAdded evt) => _state = _state.WithAdded(evt);
    private void ApplyEvent(PlannedExpenseConfirmed evt) => _state = _state.WithConfirmed(evt);
    private void ApplyEvent(PlannedExpenseCancelled evt) => _state = _state.WithCancelled(evt);

    private void MaybeSnapshot()
    {
        _eventsSinceSnapshot++;
        if (_eventsSinceSnapshot < SnapshotEvery)
        {
            return;
        }
        _eventsSinceSnapshot = 0;
        SaveSnapshot(_state);
    }

    public static Props CreateProps(Guid userId) => Props.Create(() => new UserPlannedExpensesActor(userId));

    public static Props CreatePropsFromEntityId(string entityId)
        => CreateProps(Guid.ParseExact(entityId, "N"));
}

public sealed record PlansState(IReadOnlyDictionary<Guid, PlannedExpenseView> ById)
{
    public static PlansState Empty { get; } = new(new Dictionary<Guid, PlannedExpenseView>());

    public PlansState WithAdded(PlannedExpenseAdded evt)
    {
        var next = new Dictionary<Guid, PlannedExpenseView>(ById)
        {
            [evt.PlannedId] = new PlannedExpenseView(evt.PlannedId, evt.Amount, evt.Date, evt.Description,
                PlannedStatus.Active, null)
        };
        return new PlansState(next);
    }

    public PlansState WithConfirmed(PlannedExpenseConfirmed evt)
    {
        if (!ById.TryGetValue(evt.PlannedId, out var plan))
        {
            return this;
        }
        var next = new Dictionary<Guid, PlannedExpenseView>(ById)
        {
            [evt.PlannedId] = plan with { Status = PlannedStatus.Confirmed, ConfirmedExpenseId = evt.ExpenseId }
        };
        return new PlansState(next);
    }

    public PlansState WithCancelled(PlannedExpenseCancelled evt)
    {
        if (!ById.TryGetValue(evt.PlannedId, out var plan))
        {
            return this;
        }
        var next = new Dictionary<Guid, PlannedExpenseView>(ById)
        {
            [evt.PlannedId] = plan with { Status = PlannedStatus.Cancelled }
        };
        return new PlansState(next);
    }

    public IReadOnlyList<PlannedExpenseView> AsViews() => ById.Values
        .Where(v => v.Status == PlannedStatus.Active)
        .OrderBy(v => v.Date)
        .ToArray();
}
