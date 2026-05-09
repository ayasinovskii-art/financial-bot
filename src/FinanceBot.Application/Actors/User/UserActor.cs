using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Persistence;
using FinanceBot.Application.Actors.Categorizer;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Settings;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Events;
using FinanceBot.Domain.Events.Budget;
using FinanceBot.Domain.Events.Expense;
using FinanceBot.Domain.Events.Income;
using FinanceBot.Domain.Events.User;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Per-user persistent actor. PersistenceId = "user-{userId:N}".
/// Хранит регистрационные данные, settings, и текущий бюджетный период (Stage 8+).
/// </summary>
public sealed class UserActor : ReceivePersistentActor
{
    private const int SnapshotEvery = 100;
    private static readonly ICategoryBucketMap BucketMap = new DefaultCategoryBucketMap();

    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;
    private UserState _state;
    private long _eventsSinceSnapshot;

    public override string PersistenceId { get; }

    public UserActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();
        PersistenceId = $"user-{userId:N}";
        _state = UserState.Empty;

        Recover<UserRegistered>(ApplyEvent);
        Recover<UserSettingsUpdated>(ApplyEvent);
        Recover<BudgetPeriodStarted>(ApplyEvent);
        Recover<IncomeReported>(ApplyEvent);
        Recover<BudgetAllocated>(ApplyEvent);
        Recover<ExpenseReported>(ApplyEvent);
        Recover<ExpenseCategorizedAutomatically>(ApplyEvent);
        Recover<SnapshotOffer>(offer =>
        {
            if (offer.Snapshot is UserState snap)
            {
                _state = snap;
            }
        });

        Command<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));
        Command<RegisterUser>(HandleRegister);
        Command<UpdateSettings>(HandleUpdateSettings);
        Command<ResetSettings>(HandleResetSettings);
        Command<GetUserSnapshot>(HandleGetSnapshot);
        Command<ReportIncome>(HandleReportIncome);
        Command<ReportExpense>(HandleReportExpense);
        Command<CategorizeResponse>(HandleCategorizeResponse);

        Command<Cancel>(_ => Sender.Tell(new CancelAcknowledged(_userId)));

        Command<SaveSnapshotSuccess>(_ => { });
        Command<SaveSnapshotFailure>(failure => _log.Error(failure.Cause, "User snapshot save failed."));

        CommandAny(msg => _log.Debug("UserActor[{UserId}] received unhandled {MessageType}", _userId, msg.GetType().Name));
    }

    private void HandleRegister(RegisterUser cmd)
    {
        if (_state.IsRegistered)
        {
            Sender.Tell(new UserAlreadyRegistered(_userId));
            return;
        }

        var evt = new UserRegistered(_userId, cmd.TelegramId, cmd.Timezone, DateTimeOffset.UtcNow);
        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            _log.Info("UserActor[{UserId}] registered (telegramId={TelegramId})", _userId, cmd.TelegramId);
            sender.Tell(new UserRegistrationCompleted(_userId, persisted.TelegramId));
        });
    }

    private void HandleUpdateSettings(UpdateSettings cmd)
    {
        if (!_state.IsRegistered)
        {
            Sender.Tell(new SettingsValidationFailed(_userId, cmd.Key, "Сначала зарегистрируйся через /start."));
            return;
        }

        if (!SettingsValueValidator.TryValidate(cmd.Key, cmd.Value, out var normalized, out var error))
        {
            Sender.Tell(new SettingsValidationFailed(_userId, cmd.Key, error));
            return;
        }

        var wireKey = cmd.Key.ToWireName();
        var oldValue = _state.Settings.GetValueOrDefault(wireKey);
        if (string.Equals(oldValue, normalized, StringComparison.Ordinal))
        {
            Sender.Tell(new SettingsUpdated(_userId, cmd.Key, oldValue, normalized));
            return;
        }

        var evt = new UserSettingsUpdated(_userId, wireKey, oldValue, normalized, DateTimeOffset.UtcNow);
        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            sender.Tell(new SettingsUpdated(_userId, cmd.Key, persisted.OldValue, persisted.NewValue));
        });
    }

    private void HandleResetSettings(ResetSettings cmd)
    {
        if (!_state.IsRegistered)
        {
            Sender.Tell(new SettingsValidationFailed(
                _userId, cmd.Key ?? SettingsKey.Timezone, "Сначала зарегистрируйся через /start."));
            return;
        }

        var sender = Sender;
        if (cmd.Key is { } key)
        {
            var wireKey = key.ToWireName();
            var old = _state.Settings.GetValueOrDefault(wireKey);
            var evt = new UserSettingsUpdated(_userId, wireKey, old, null, DateTimeOffset.UtcNow);
            Persist(evt, persisted =>
            {
                ApplyEvent(persisted);
                MaybeSnapshot();
                sender.Tell(new SettingsResetCompleted(_userId, key));
            });
            return;
        }

        var setKeys = _state.Settings.Where(kv => kv.Value is not null).Select(kv => kv.Key).ToArray();
        if (setKeys.Length == 0)
        {
            sender.Tell(new SettingsResetCompleted(_userId, null));
            return;
        }

        var events = setKeys
            .Select(k => new UserSettingsUpdated(_userId, k, _state.Settings[k], null, DateTimeOffset.UtcNow))
            .ToArray();

        var pending = events.Length;
        PersistAll(events, persisted =>
        {
            ApplyEvent(persisted);
            if (Interlocked.Decrement(ref pending) == 0)
            {
                MaybeSnapshot();
                sender.Tell(new SettingsResetCompleted(_userId, null));
            }
        });
    }

    private void HandleReportIncome(ReportIncome cmd)
    {
        if (!_state.IsRegistered)
        {
            Sender.Tell(new IncomeRejected(_userId, "Сначала зарегистрируйся через /start."));
            return;
        }
        if (cmd.Amount <= 0m)
        {
            Sender.Tell(new IncomeRejected(_userId, "Сумма должна быть положительной."));
            return;
        }

        var sender = Sender;
        var occurredAt = cmd.OccurredAt;
        var startDate = DateOnly.FromDateTime(occurredAt.LocalDateTime);
        var ratios = ParseAllocationOrDefault(_state.Settings.GetValueOrDefault(SettingsKey.Allocation.ToWireName()));

        var events = new List<IDomainEvent>(3);
        Guid periodId;
        DateOnly periodStart;
        decimal newTotal;

        if (_state.ActivePeriod is null)
        {
            periodId = Guid.NewGuid();
            periodStart = startDate;
            events.Add(new BudgetPeriodStarted(_userId, periodId, periodStart, PeriodType.SalaryCycle, occurredAt));
            newTotal = cmd.Amount;
        }
        else
        {
            periodId = _state.ActivePeriod.PeriodId;
            periodStart = _state.ActivePeriod.StartDate;
            newTotal = _state.ActivePeriod.TotalIncome + cmd.Amount;
        }

        var incomeId = Guid.NewGuid();
        events.Add(new IncomeReported(_userId, incomeId, periodId, cmd.Amount, occurredAt, cmd.Description));

        var (essentials, fun, deposit) = ratios.ApplyTo(newTotal);
        events.Add(new BudgetAllocated(_userId, periodId, newTotal, essentials, fun, deposit, occurredAt));

        var pending = events.Count;
        PersistAll(events, persisted =>
        {
            ApplyEvent(persisted);
            if (Interlocked.Decrement(ref pending) == 0)
            {
                MaybeSnapshot();
                sender.Tell(new IncomeAccepted(
                    _userId, incomeId, periodId, periodStart, newTotal, essentials, fun, deposit));
            }
        });
    }

    private readonly Dictionary<Guid, PendingCategorization> _pendingCategorizationSenders = new();

    private sealed record PendingCategorization(IActorRef Sender, decimal Amount);

    private void HandleReportExpense(ReportExpense cmd)
    {
        if (!_state.IsRegistered)
        {
            Sender.Tell(new ExpenseRejected(_userId, "Сначала зарегистрируйся через /start."));
            return;
        }
        if (cmd.Amount <= 0m)
        {
            Sender.Tell(new ExpenseRejected(_userId, "Сумма должна быть положительной."));
            return;
        }
        if (_state.ActivePeriod is null)
        {
            Sender.Tell(new ExpenseRejected(_userId, "Сначала зафиксируй доход через /income — нет активного периода."));
            return;
        }

        var expenseId = Guid.NewGuid();
        var description = string.IsNullOrWhiteSpace(cmd.Description) ? "(без описания)" : cmd.Description;
        var evt = new ExpenseReported(
            UserId: _userId,
            ExpenseId: expenseId,
            PeriodId: _state.ActivePeriod.PeriodId,
            Amount: cmd.Amount,
            OccurredAt: cmd.OccurredAt,
            Description: description,
            Source: cmd.Source,
            EventVersion: 1);

        var sender = Sender;
        _pendingCategorizationSenders[expenseId] = new PendingCategorization(sender, cmd.Amount);

        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();

            var registry = ActorRegistry.For(Context.System);
            if (!registry.TryGet<CategorizerActorMarker>(out var categorizer))
            {
                _log.Warning("CategorizerActor not registered; falling back to Other.");
                CompleteExpenseWithCategory(expenseId, Category.Other, ExpenseSource.Fallback, needsReview: true);
                return;
            }

            var normalized = NormalizedDescription.FromRaw(persisted.Description);
            categorizer.Tell(new CategorizeRequest(Guid.NewGuid(), _userId, expenseId, normalized));
        });
    }

    private void HandleCategorizeResponse(CategorizeResponse resp)
    {
        if (resp.UserId != _userId)
        {
            return;
        }
        CompleteExpenseWithCategory(resp.ExpenseId, resp.Category, resp.Source, resp.NeedsReview);
    }

    private void CompleteExpenseWithCategory(Guid expenseId, Category category, ExpenseSource source, bool needsReview)
    {
        var evt = new ExpenseCategorizedAutomatically(
            UserId: _userId,
            ExpenseId: expenseId,
            Category: category,
            Source: source,
            NeedsReview: needsReview,
            OccurredAt: DateTimeOffset.UtcNow);

        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();

            if (!_pendingCategorizationSenders.Remove(expenseId, out var pending) || _state.ActivePeriod is null)
            {
                return;
            }

            var bucket = BucketMap.Map(persisted.Category);
            pending.Sender.Tell(new ExpenseAccepted(
                _userId, expenseId, _state.ActivePeriod.PeriodId, pending.Amount,
                persisted.Category, bucket,
                _state.ActivePeriod.SpentEssentials, _state.ActivePeriod.SpentFun, _state.ActivePeriod.SpentDeposit,
                _state.ActivePeriod.AllocationEssentials, _state.ActivePeriod.AllocationFun, _state.ActivePeriod.AllocationDeposit));
        });
    }

    private void HandleGetSnapshot(GetUserSnapshot _)
        => Sender.Tell(new UserSnapshot(
            _userId, _state.IsRegistered, _state.TelegramId, _state.Timezone, _state.Settings));

    private void ApplyEvent(IDomainEvent evt)
    {
        _state = evt switch
        {
            UserRegistered r => _state.WithRegistration(r),
            UserSettingsUpdated s => _state.WithSettings(s.Key, s.NewValue),
            BudgetPeriodStarted p => _state.WithNewPeriod(p),
            IncomeReported i => _state.WithIncome(i),
            BudgetAllocated a => _state.WithAllocation(a),
            ExpenseReported e => _state.WithReportedExpense(e),
            ExpenseCategorizedAutomatically c => _state.WithCategorizedExpense(c, BucketMap.Map(c.Category)),
            _ => _state
        };
    }

    private static AllocationRatios ParseAllocationOrDefault(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return AllocationRatios.Default;
        }
        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var e)
            || !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var f)
            || !int.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return AllocationRatios.Default;
        }
        try
        {
            return new AllocationRatios(e, f, d);
        }
        catch (ArgumentException)
        {
            return AllocationRatios.Default;
        }
    }

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

    public static Props CreateProps(Guid userId) => Props.Create(() => new UserActor(userId));

    public static Props CreatePropsFromEntityId(string entityId)
        => CreateProps(Guid.ParseExact(entityId, "N"));
}

/// <summary>Активный бюджетный период.</summary>
public sealed record ActivePeriod(
    Guid PeriodId,
    DateOnly StartDate,
    PeriodType Type,
    decimal TotalIncome,
    decimal AllocationEssentials,
    decimal AllocationFun,
    decimal AllocationDeposit,
    decimal SpentEssentials,
    decimal SpentFun,
    decimal SpentDeposit,
    IReadOnlyDictionary<Guid, decimal> PendingAmounts);

/// <summary>Состояние UserActor (in-memory). Восстанавливается из событий.</summary>
public sealed record UserState(
    bool IsRegistered,
    long? TelegramId,
    string? Timezone,
    DateTimeOffset? RegisteredAt,
    Dictionary<string, string?> Settings,
    ActivePeriod? ActivePeriod)
{
    public static UserState Empty { get; } = new(false, null, null, null,
        new Dictionary<string, string?>(StringComparer.Ordinal), null);

    public UserState WithRegistration(UserRegistered r)
        => this with { IsRegistered = true, TelegramId = r.TelegramId, Timezone = r.Timezone, RegisteredAt = r.OccurredAt };

    public UserState WithSettings(string key, string? value)
    {
        var next = new Dictionary<string, string?>(Settings, StringComparer.Ordinal);
        if (value is null)
        {
            next.Remove(key);
        }
        else
        {
            next[key] = value;
        }
        return this with { Settings = next };
    }

    public UserState WithNewPeriod(BudgetPeriodStarted p)
        => this with
        {
            ActivePeriod = new ActivePeriod(
                p.PeriodId, p.StartDate, p.PeriodType,
                0m, 0m, 0m, 0m, 0m, 0m, 0m,
                new Dictionary<Guid, decimal>())
        };

    public UserState WithReportedExpense(ExpenseReported e)
    {
        // Категория ещё неизвестна — в spent попадает только после ExpenseCategorizedAutomatically.
        // Запоминаем сумму в pending словаре (хранится отдельно в ActivePeriod.PendingAmounts).
        if (ActivePeriod is null || ActivePeriod.PeriodId != e.PeriodId)
        {
            return this;
        }
        var pending = new Dictionary<Guid, decimal>(ActivePeriod.PendingAmounts) { [e.ExpenseId] = e.Amount };
        return this with { ActivePeriod = ActivePeriod with { PendingAmounts = pending } };
    }

    public UserState WithCategorizedExpense(ExpenseCategorizedAutomatically c, Bucket bucket)
    {
        if (ActivePeriod is null)
        {
            return this;
        }
        if (!ActivePeriod.PendingAmounts.TryGetValue(c.ExpenseId, out var amount))
        {
            return this;
        }
        var nextPending = new Dictionary<Guid, decimal>(ActivePeriod.PendingAmounts);
        nextPending.Remove(c.ExpenseId);
        var p = ActivePeriod with { PendingAmounts = nextPending };
        p = bucket switch
        {
            Bucket.Essentials => p with { SpentEssentials = p.SpentEssentials + amount },
            Bucket.Fun => p with { SpentFun = p.SpentFun + amount },
            Bucket.Deposit => p with { SpentDeposit = p.SpentDeposit + amount },
            _ => p
        };
        return this with { ActivePeriod = p };
    }

    public UserState WithIncome(IncomeReported i)
    {
        if (ActivePeriod is null || ActivePeriod.PeriodId != i.PeriodId)
        {
            return this;
        }
        return this with
        {
            ActivePeriod = ActivePeriod with { TotalIncome = ActivePeriod.TotalIncome + i.Amount }
        };
    }

    public UserState WithAllocation(BudgetAllocated a)
    {
        if (ActivePeriod is null || ActivePeriod.PeriodId != a.PeriodId)
        {
            return this;
        }
        return this with
        {
            ActivePeriod = ActivePeriod with
            {
                TotalIncome = a.TotalIncome,
                AllocationEssentials = a.AllocationEssentials,
                AllocationFun = a.AllocationFun,
                AllocationDeposit = a.AllocationDeposit
            }
        };
    }
}

/// <summary>Reply: пользователь успешно зарегистрирован.</summary>
public sealed record UserRegistrationCompleted(Guid UserId, long TelegramId);

/// <summary>Reply: пользователь уже был зарегистрирован раньше.</summary>
public sealed record UserAlreadyRegistered(Guid UserId);

/// <summary>Reply: команда Cancel принята.</summary>
public sealed record CancelAcknowledged(Guid UserId);
