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
/// Ядро (регистрация, settings, доходы/расходы, категоризация, savings) — здесь.
/// Фиче-расширения: EveningSurvey (FSM вечернего опроса), WakeupRecovery (детект простоя),
/// Advice, Charts, Reports — в одноимённых partial-файлах.
/// </summary>
public sealed partial class UserActor : ReceivePersistentActor, IWithTimers
{
    private const int SnapshotEvery = 100;
    private static readonly TimeSpan CategorizationDeadlineDelay = TimeSpan.FromSeconds(45);
    private static readonly ICategoryBucketMap BucketMap = new DefaultCategoryBucketMap();

    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;
    private UserState _state;
    private long _eventsSinceSnapshot;

    public ITimerScheduler Timers { get; set; } = null!;

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
        Recover<ExpenseCategoryCorrected>(ApplyEvent);
        Recover<SavingsReported>(ApplyEvent);
        Recover<BudgetPeriodClosed>(ApplyEvent);
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
        Command<CategorizationDeadline>(HandleCategorizationDeadline);
        Command<CorrectExpenseCategory>(HandleCorrectExpenseCategory);
        Command<GetNeedsReviewExpenses>(HandleGetNeedsReview);
        Command<ConfirmSavings>(HandleConfirmSavings);

        Command<Cancel>(_ => Sender.Tell(new CancelAcknowledged(_userId)));

        Command<SaveSnapshotSuccess>(_ => { });
        Command<SaveSnapshotFailure>(failure => _log.Error(failure.Cause, "User snapshot save failed."));

        WireEveningSurvey();
        WireWakeupRecovery();
        WireAdvice();
        WireCharts();
        WireReports();

        CommandAny(msg => _log.Debug("UserActor[{UserId}] received unhandled {MessageType}", _userId, msg.GetType().Name));
    }

    partial void WireEveningSurvey();
    partial void WireWakeupRecovery();
    partial void WireAdvice();
    partial void WireCharts();
    partial void WireReports();

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

        var events = new List<IDomainEvent>(4);
        Guid periodId;
        DateOnly periodStart;
        decimal newTotal;

        var openNewPeriod = _state.ActivePeriod is null || _state.PeriodClosable;

        if (openNewPeriod)
        {
            // Если активный период существует и помечен closable (после /savings) — сначала закрываем его.
            if (_state.ActivePeriod is { } prev)
            {
                events.Add(new BudgetPeriodClosed(
                    _userId, prev.PeriodId, startDate.AddDays(-1),
                    SummaryJson: BuildClosedSummaryJson(prev), OccurredAt: occurredAt));
            }
            periodId = Guid.NewGuid();
            periodStart = startDate;
            events.Add(new BudgetPeriodStarted(_userId, periodId, periodStart, PeriodType.SalaryCycle, occurredAt));
            newTotal = cmd.Amount;
        }
        else
        {
            periodId = _state.ActivePeriod!.PeriodId;
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

            var normalized = NormalizedDescription.FromRaw(persisted.Description);

            // Сначала memory.
            if (!normalized.IsEmpty
                && _state.CategoryMemory.TryGetValue(normalized.Value, out var memCategory))
            {
                CompleteExpenseWithCategory(expenseId, normalized, memCategory, ExpenseSource.Memory, needsReview: false);
                return;
            }

            var registry = ActorRegistry.For(Context.System);
            if (!registry.TryGet<CategorizerActorMarker>(out var categorizer))
            {
                _log.Warning("CategorizerActor not registered; falling back to Other.");
                CompleteExpenseWithCategory(expenseId, normalized, Category.Other, ExpenseSource.Fallback, needsReview: true);
                return;
            }

            Timers.StartSingleTimer(CategorizationTimerKey(expenseId),
                new CategorizationDeadline(expenseId), CategorizationDeadlineDelay);
            categorizer.Tell(new CategorizeRequest(Guid.NewGuid(), _userId, expenseId, normalized));
        });
    }

    private void HandleCategorizationDeadline(CategorizationDeadline d)
    {
        if (!_pendingCategorizationSenders.Remove(d.ExpenseId, out var pending))
        {
            return;
        }
        _log.Warning("Categorization deadline reached for expense {ExpenseId}; replying fallback.", d.ExpenseId);
        if (_state.ActivePeriod is { } period)
        {
            var bucket = BucketMap.Map(Category.Other);
            pending.Sender.Tell(new ExpenseAccepted(
                _userId, d.ExpenseId, period.PeriodId, pending.Amount,
                Category.Other, bucket,
                period.SpentEssentials, period.SpentFun, period.SpentDeposit,
                period.AllocationEssentials, period.AllocationFun, period.AllocationDeposit));
        }
        else
        {
            pending.Sender.Tell(new ExpenseRejected(_userId, "Категоризация не завершилась вовремя."));
        }
    }

    private static string CategorizationTimerKey(Guid expenseId) => $"cat-deadline-{expenseId:N}";

    private sealed record CategorizationDeadline(Guid ExpenseId);

    private void HandleCategorizeResponse(CategorizeResponse resp)
    {
        if (resp.UserId != _userId)
        {
            return;
        }
        // Описание берём из state.PendingDescriptions, заполненного при ExpenseReported.
        var normalized = _state.ActivePeriod is { } period
                         && period.PendingDescriptions.TryGetValue(resp.ExpenseId, out var n)
            ? n
            : default;
        CompleteExpenseWithCategory(resp.ExpenseId, normalized, resp.Category, resp.Source, resp.NeedsReview);
    }

    private void CompleteExpenseWithCategory(
        Guid expenseId,
        NormalizedDescription normalized,
        Category category,
        ExpenseSource source,
        bool needsReview)
    {
        var evt = new ExpenseCategorizedAutomatically(
            UserId: _userId,
            ExpenseId: expenseId,
            Category: category,
            Source: source,
            NeedsReview: needsReview,
            NormalizedDescription: normalized,
            OccurredAt: DateTimeOffset.UtcNow);

        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();

            Timers.Cancel(CategorizationTimerKey(expenseId));
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

    private void HandleCorrectExpenseCategory(CorrectExpenseCategory cmd)
    {
        if (!_state.IsRegistered || _state.ActivePeriod is null)
        {
            Sender.Tell(new ExpenseCorrectionRejected(_userId, "Активного периода нет."));
            return;
        }

        var review = _state.ActivePeriod.NeedsReviewExpenses
            .FirstOrDefault(r => r.ExpenseId == cmd.ExpenseId);
        if (review is null)
        {
            Sender.Tell(new ExpenseCorrectionRejected(_userId,
                "Эта трата уже не требует ручной категоризации (или принадлежит другому периоду)."));
            return;
        }

        var evt = new ExpenseCategoryCorrected(
            UserId: _userId,
            ExpenseId: cmd.ExpenseId,
            OldCategory: review.Category,
            NewCategory: cmd.NewCategory,
            NormalizedDescription: review.NormalizedDescription,
            OccurredAt: DateTimeOffset.UtcNow);

        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            sender.Tell(new ExpenseCorrectionApplied(_userId, persisted.ExpenseId, persisted.OldCategory, persisted.NewCategory));
        });
    }

    private void HandleGetNeedsReview(GetNeedsReviewExpenses cmd)
    {
        if (_state.ActivePeriod is null)
        {
            Sender.Tell(new NeedsReviewList(_userId, []));
            return;
        }

        var top = _state.ActivePeriod.NeedsReviewExpenses
            .OrderByDescending(e => e.OccurredAt)
            .Take(cmd.Limit)
            .ToArray();
        Sender.Tell(new NeedsReviewList(_userId, top));
    }

    private void HandleGetSnapshot(GetUserSnapshot _)
        => Sender.Tell(new UserSnapshot(
            _userId, _state.IsRegistered, _state.TelegramId, _state.Timezone, _state.Settings));

    private void HandleConfirmSavings(ConfirmSavings cmd)
    {
        if (!_state.IsRegistered)
        {
            Sender.Tell(new SavingsRejected(_userId, "Сначала зарегистрируйся через /start."));
            return;
        }
        if (cmd.Amount < 0m)
        {
            Sender.Tell(new SavingsRejected(_userId, "Сумма не может быть отрицательной."));
            return;
        }
        var period = _state.ActivePeriod;
        if (period is null)
        {
            Sender.Tell(new SavingsRejected(_userId, "Активного периода нет."));
            return;
        }

        // Guid.Empty используется UI как «текущий период».
        var resolvedPeriodId = cmd.PeriodId == Guid.Empty ? period.PeriodId : cmd.PeriodId;
        if (resolvedPeriodId != period.PeriodId)
        {
            Sender.Tell(new SavingsRejected(_userId, "Период не совпадает с активным."));
            return;
        }

        var evt = new SavingsReported(_userId, resolvedPeriodId, cmd.Amount, DateTimeOffset.UtcNow);
        var sender = Sender;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            sender.Tell(new SavingsAccepted(_userId, persisted.PeriodId, persisted.Amount));
        });
    }

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
            ExpenseCategoryCorrected cc => _state.WithCorrectedExpense(cc, BucketMap.Map(cc.OldCategory), BucketMap.Map(cc.NewCategory)),
            SavingsReported sr => _state with { PeriodClosable = _state.ActivePeriod?.PeriodId == sr.PeriodId },
            BudgetPeriodClosed _ => _state with { ActivePeriod = null, PeriodClosable = false },
            _ => _state
        };
    }

    private static string BuildClosedSummaryJson(ActivePeriod p)
    {
        // Лёгкая сериализация по необходимым полям.
        var doc = new
        {
            periodId = p.PeriodId,
            startDate = p.StartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            totalIncome = p.TotalIncome,
            spentEssentials = p.SpentEssentials,
            spentFun = p.SpentFun,
            spentDeposit = p.SpentDeposit
        };
        return System.Text.Json.JsonSerializer.Serialize(doc);
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
    IReadOnlyDictionary<Guid, decimal> PendingAmounts,
    IReadOnlyDictionary<Guid, NormalizedDescription> PendingDescriptions,
    IReadOnlyList<NeedsReviewExpense> NeedsReviewExpenses);

/// <summary>Запись о трате, требующей ручной категоризации.</summary>
public sealed record NeedsReviewExpense(
    Guid ExpenseId,
    decimal Amount,
    string Description,
    NormalizedDescription NormalizedDescription,
    Category Category,
    DateTimeOffset OccurredAt);

/// <summary>Состояние UserActor (in-memory). Восстанавливается из событий.</summary>
public sealed record UserState(
    bool IsRegistered,
    long? TelegramId,
    string? Timezone,
    DateTimeOffset? RegisteredAt,
    Dictionary<string, string?> Settings,
    ActivePeriod? ActivePeriod,
    IReadOnlyDictionary<string, Category> CategoryMemory,
    bool PeriodClosable)
{
    public static UserState Empty { get; } = new(
        false, null, null, null,
        new Dictionary<string, string?>(StringComparer.Ordinal),
        null,
        new Dictionary<string, Category>(StringComparer.Ordinal),
        PeriodClosable: false);

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
                new Dictionary<Guid, decimal>(),
                new Dictionary<Guid, NormalizedDescription>(),
                Array.Empty<NeedsReviewExpense>())
        };

    public UserState WithReportedExpense(ExpenseReported e)
    {
        if (ActivePeriod is null || ActivePeriod.PeriodId != e.PeriodId)
        {
            return this;
        }
        var pendingAmounts = new Dictionary<Guid, decimal>(ActivePeriod.PendingAmounts) { [e.ExpenseId] = e.Amount };
        var pendingDescs = new Dictionary<Guid, NormalizedDescription>(ActivePeriod.PendingDescriptions)
        {
            [e.ExpenseId] = NormalizedDescription.FromRaw(e.Description)
        };
        var period = ActivePeriod with
        {
            PendingAmounts = pendingAmounts,
            PendingDescriptions = pendingDescs
        };
        return this with { ActivePeriod = period };
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

        var nextAmounts = new Dictionary<Guid, decimal>(ActivePeriod.PendingAmounts);
        nextAmounts.Remove(c.ExpenseId);
        var nextDescs = new Dictionary<Guid, NormalizedDescription>(ActivePeriod.PendingDescriptions);
        nextDescs.Remove(c.ExpenseId);

        var p = ActivePeriod with { PendingAmounts = nextAmounts, PendingDescriptions = nextDescs };
        p = bucket switch
        {
            Bucket.Essentials => p with { SpentEssentials = p.SpentEssentials + amount },
            Bucket.Fun => p with { SpentFun = p.SpentFun + amount },
            Bucket.Deposit => p with { SpentDeposit = p.SpentDeposit + amount },
            _ => p
        };

        if (c.NeedsReview)
        {
            var review = new NeedsReviewExpense(
                c.ExpenseId, amount, c.NormalizedDescription.Value, c.NormalizedDescription, c.Category, c.OccurredAt);
            var list = p.NeedsReviewExpenses.Concat([review]).ToArray();
            p = p with { NeedsReviewExpenses = list };
        }

        var nextMemory = MaybeUpdateMemory(c.NormalizedDescription, c.Source, c.Category);
        return this with { ActivePeriod = p, CategoryMemory = nextMemory };
    }

    public UserState WithCorrectedExpense(ExpenseCategoryCorrected c, Bucket oldBucket, Bucket newBucket)
    {
        var nextMemory = AddMemory(c.NormalizedDescription, c.NewCategory);
        if (ActivePeriod is null)
        {
            return this with { CategoryMemory = nextMemory };
        }

        var review = ActivePeriod.NeedsReviewExpenses.FirstOrDefault(e => e.ExpenseId == c.ExpenseId);
        var p = ActivePeriod;
        if (review is not null)
        {
            p = p with { NeedsReviewExpenses = ActivePeriod.NeedsReviewExpenses.Where(e => e.ExpenseId != c.ExpenseId).ToArray() };

            // Перенос spent между бакетами при изменении категории.
            if (oldBucket != newBucket)
            {
                p = SubtractFromBucket(p, oldBucket, review.Amount);
                p = AddToBucket(p, newBucket, review.Amount);
            }
        }
        return this with { ActivePeriod = p, CategoryMemory = nextMemory };
    }

    private IReadOnlyDictionary<string, Category> MaybeUpdateMemory(
        NormalizedDescription normalized, ExpenseSource source, Category category)
    {
        // Memory обновляется на ExpenseCategorizedAutomatically с source ∈ {Memory, Claude}.
        // Source = Rules не меняет memory (правила и так детерминируют категоризацию).
        if (source is not ExpenseSource.Memory and not ExpenseSource.Claude)
        {
            return CategoryMemory;
        }
        if (normalized.IsEmpty)
        {
            return CategoryMemory;
        }
        return AddMemory(normalized, category);
    }

    private IReadOnlyDictionary<string, Category> AddMemory(NormalizedDescription normalized, Category category)
    {
        if (normalized.IsEmpty)
        {
            return CategoryMemory;
        }
        var next = new Dictionary<string, Category>(CategoryMemory, StringComparer.Ordinal)
        {
            [normalized.Value] = category
        };
        return next;
    }

    private static ActivePeriod SubtractFromBucket(ActivePeriod p, Bucket bucket, decimal amount) => bucket switch
    {
        Bucket.Essentials => p with { SpentEssentials = p.SpentEssentials - amount },
        Bucket.Fun => p with { SpentFun = p.SpentFun - amount },
        Bucket.Deposit => p with { SpentDeposit = p.SpentDeposit - amount },
        _ => p
    };

    private static ActivePeriod AddToBucket(ActivePeriod p, Bucket bucket, decimal amount) => bucket switch
    {
        Bucket.Essentials => p with { SpentEssentials = p.SpentEssentials + amount },
        Bucket.Fun => p with { SpentFun = p.SpentFun + amount },
        Bucket.Deposit => p with { SpentDeposit = p.SpentDeposit + amount },
        _ => p
    };

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
