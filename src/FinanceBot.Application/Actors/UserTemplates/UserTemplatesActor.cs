using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Persistence;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Application.Actors.UserTemplates.Messages;
using FinanceBot.Domain.Commands.Templates;
using FinanceBot.Domain.Events.Recurring;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.UserTemplates;

/// <summary>
/// Per-user persistent actor для регулярных шаблонов трат. PersistenceId = "user-templates-{userId:N}".
/// Хранит в state Dictionary&lt;TemplateId, RecurringTemplate&gt;; команды Add/Remove/List;
/// query GetRelevantTemplates(date) с учётом IWorkdayCalendar для weekdays-расписаний.
/// </summary>
public sealed class UserTemplatesActor : ReceivePersistentActor
{
    private const int SnapshotEvery = 100;

    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;
    private TemplatesState _state = TemplatesState.Empty;
    private long _eventsSinceSnapshot;

    public override string PersistenceId { get; }

    public UserTemplatesActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();
        PersistenceId = $"user-templates-{userId:N}";

        Recover<RecurringTemplateAdded>(ApplyEvent);
        Recover<RecurringTemplateRemoved>(ApplyEvent);
        Recover<SnapshotOffer>(offer =>
        {
            if (offer.Snapshot is TemplatesState snap)
            {
                _state = snap;
            }
        });

        Command<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));
        Command<AddTemplate>(HandleAdd);
        Command<RemoveTemplate>(HandleRemove);
        Command<ListTemplates>(_ => Sender.Tell(new TemplateList(_userId, _state.AsViews())));
        Command<GetRelevantTemplates>(HandleGetRelevant);

        Command<SaveSnapshotSuccess>(_ => { });
        Command<SaveSnapshotFailure>(failure => _log.Error(failure.Cause, "Templates snapshot save failed."));

        CommandAny(msg => _log.Debug("UserTemplatesActor[{UserId}] received unhandled {MessageType}", _userId, msg.GetType().Name));
    }

    private void HandleAdd(AddTemplate cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
        {
            Sender.Tell(new TemplateRejected(_userId, "Имя шаблона не может быть пустым."));
            return;
        }
        if (cmd.Amount <= 0m)
        {
            Sender.Tell(new TemplateRejected(_userId, "Сумма должна быть положительной."));
            return;
        }
        if (_state.ByName.ContainsKey(cmd.Name))
        {
            Sender.Tell(new TemplateRejected(_userId, $"Шаблон '{cmd.Name}' уже существует."));
            return;
        }

        var templateId = Guid.NewGuid();
        var evt = new RecurringTemplateAdded(
            UserId: _userId,
            TemplateId: templateId,
            Name: cmd.Name,
            Amount: cmd.Amount,
            Schedule: cmd.Schedule,
            Category: cmd.Category,
            OccurredAt: DateTimeOffset.UtcNow);

        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            sender.Tell(new TemplateAdded(_userId,
                new RecurringTemplateView(templateId, cmd.Name, cmd.Amount, cmd.Schedule, cmd.Category)));
        });
    }

    private void HandleRemove(RemoveTemplate cmd)
    {
        if (!_state.ByName.TryGetValue(cmd.Name, out var template))
        {
            Sender.Tell(new TemplateRejected(_userId, $"Шаблон '{cmd.Name}' не найден."));
            return;
        }

        var evt = new RecurringTemplateRemoved(_userId, template.TemplateId, DateTimeOffset.UtcNow);
        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            sender.Tell(new TemplateRemoved(_userId, cmd.Name));
        });
    }

    private void HandleGetRelevant(GetRelevantTemplates query)
    {
        var registry = ActorRegistry.For(Context.System);
        registry.TryGet<IWorkdayCalendar>(out var calendarRef);
        // Календарь — DI service, но у нас он singleton; забираем напрямую из ServiceProvider через DI-resolver если есть.
        // Для простоты: все сценарии кроме WeekdaysSchedule детерминированы по дате. Для weekdays — спросим календарь синхронно через task.

        var date = query.Date;
        var sender = Sender;

        Task.Run(async () =>
        {
            var calendar = TryResolveCalendar(Context.System);
            var matched = new List<RecurringTemplateView>();
            foreach (var t in _state.ByName.Values)
            {
                if (await IsActiveAsync(t.Schedule, date, calendar).ConfigureAwait(false))
                {
                    matched.Add(t);
                }
            }
            sender.Tell(new RelevantTemplatesList(_userId, date, matched));
        });
        _ = calendarRef;
    }

    private static IWorkdayCalendar? TryResolveCalendar(ActorSystem system)
    {
        // Пытаемся достать из DependencyResolver (Akka.Hosting).
        return Akka.DependencyInjection.DependencyResolver.For(system)
            .Resolver.GetService(typeof(IWorkdayCalendar)) as IWorkdayCalendar;
    }

    private static async Task<bool> IsActiveAsync(ScheduleSpec schedule, DateOnly date, IWorkdayCalendar? calendar)
    {
        switch (schedule)
        {
            case DailySchedule:
                return true;

            case WeekdaysSchedule:
                if (calendar is null)
                {
                    var d = date.DayOfWeek;
                    return d != DayOfWeek.Saturday && d != DayOfWeek.Sunday;
                }
                return await calendar.IsWorkdayAsync(date, CancellationToken.None).ConfigureAwait(false);

            case DaysOfWeekSchedule dow:
                {
                    var iso = (int)date.DayOfWeek;
                    if (iso == 0)
                    {
                        iso = 7; // ISO: Sunday=7
                    }
                    return dow.ContainsIsoDay(iso);
                }

            case DaysOfMonthSchedule dom:
                return dom.ContainsDay(date.Day);

            default:
                return false;
        }
    }

    private void ApplyEvent(RecurringTemplateAdded evt) => _state = _state.WithAdded(evt);
    private void ApplyEvent(RecurringTemplateRemoved evt) => _state = _state.WithRemoved(evt);

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

    public static Props CreateProps(Guid userId) => Props.Create(() => new UserTemplatesActor(userId));

    public static Props CreatePropsFromEntityId(string entityId)
        => CreateProps(Guid.ParseExact(entityId, "N"));
}

/// <summary>State актора шаблонов.</summary>
public sealed record TemplatesState(IReadOnlyDictionary<string, RecurringTemplateView> ByName)
{
    public static TemplatesState Empty { get; } = new(new Dictionary<string, RecurringTemplateView>(StringComparer.Ordinal));

    public TemplatesState WithAdded(RecurringTemplateAdded evt)
    {
        var next = new Dictionary<string, RecurringTemplateView>(ByName, StringComparer.Ordinal)
        {
            [evt.Name] = new RecurringTemplateView(evt.TemplateId, evt.Name, evt.Amount, evt.Schedule, evt.Category)
        };
        return new TemplatesState(next);
    }

    public TemplatesState WithRemoved(RecurringTemplateRemoved evt)
    {
        var entry = ByName.FirstOrDefault(kv => kv.Value.TemplateId == evt.TemplateId);
        if (entry.Key is null)
        {
            return this;
        }
        var next = new Dictionary<string, RecurringTemplateView>(ByName, StringComparer.Ordinal);
        next.Remove(entry.Key);
        return new TemplatesState(next);
    }

    public IReadOnlyList<RecurringTemplateView> AsViews() => ByName.Values.ToArray();
}
