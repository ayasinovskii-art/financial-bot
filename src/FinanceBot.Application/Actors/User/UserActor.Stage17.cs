using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Persistence;
using FinanceBot.Application.Actors.Categorizer;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Application.Actors.UserPlannedExpenses.Messages;
using FinanceBot.Application.Actors.UserTemplates.Messages;
using FinanceBot.Application.Configuration;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Events;
using FinanceBot.Domain.Events.Expense;
using FinanceBot.Domain.Events.Recurring;
using FinanceBot.Domain.Events.Scheduling;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Stage 17: вечерний опрос. FSM Idle ↔ AwaitingDailyExpenses через BecomeStacked + Stash.
/// На EveningTickFired → запрос шаблонов/планов на сегодня → формирование вопроса → переход в FSM.
/// На свободный текст пользователя (ReportExpense) → обработка + выход. На SilenceDeadlineFired
/// и auto_confirm_on_silence — авто-фиксация шаблонов. На /cancel — выход без записи.
/// </summary>
public sealed partial class UserActor
{
    private static readonly TimeSpan EveningAskTimeout = TimeSpan.FromSeconds(3);

    private ICancelable? _silenceDeadlineHandle;
    private DateOnly _activeEveningDate;

    partial void WireStage17()
    {
        Recover<EveningQuestionAsked>(_ => { /* no state change, marker only */ });
        Recover<RecurringExpenseAutoConfirmed>(_ => { /* informational */ });

        Command<EveningTickFired>(OnEveningTickInIdle);
        Command<EveningPromptPrepared>(OnEveningPromptPrepared);
        Command<SalaryDayTickFired>(OnSalaryDayTick);
        Command<SilenceDeadlineFired>(_ => { /* ignore in Idle */ });
    }

    private void OnEveningTickInIdle(EveningTickFired tick)
    {
        if (!_state.IsRegistered || _state.TelegramId is not { } chatId)
        {
            return;
        }

        var settings = UserScheduleSettings.FromDictionary(_state.Settings, ResolveDefaultTimezone());
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(tick.OccurredAt, settings.Timezone).LocalDateTime);
        _activeEveningDate = date;

        var registry = ActorRegistry.For(Context.System);
        var hasTemplates = registry.TryGet<UserTemplatesShardMarker>(out var templatesShard);
        var hasPlanned = registry.TryGet<UserPlannedShardMarker>(out var plannedShard);
        var templatesRef = hasTemplates ? templatesShard : null;
        var plannedRef = hasPlanned ? plannedShard : null;

        var userId = _userId;
        var self = Self;

        Task.Run(async () =>
        {
            var templates = await TryAskTemplatesAsync(templatesRef, userId, date).ConfigureAwait(false);
            var plans = await TryAskPlansAsync(plannedRef, userId, date).ConfigureAwait(false);
            self.Tell(new EveningPromptPrepared(date, chatId, templates, plans, settings, tick.OccurredAt));
        });
    }

    private static async Task<IReadOnlyList<RecurringTemplateView>> TryAskTemplatesAsync(
        IActorRef? shard, Guid userId, DateOnly date)
    {
        if (shard is null || shard.Equals(ActorRefs.Nobody))
        {
            return Array.Empty<RecurringTemplateView>();
        }
        try
        {
            var reply = await shard.Ask<RelevantTemplatesList>(
                new ShardEnvelope(userId.ToString("N"), new GetRelevantTemplates(userId, date)),
                EveningAskTimeout).ConfigureAwait(false);
            return reply.Templates;
        }
        catch (AskTimeoutException)
        {
            return Array.Empty<RecurringTemplateView>();
        }
        catch (NotSupportedException)
        {
            return Array.Empty<RecurringTemplateView>();
        }
    }

    private static async Task<IReadOnlyList<PlannedExpenseView>> TryAskPlansAsync(
        IActorRef? shard, Guid userId, DateOnly date)
    {
        if (shard is null || shard.Equals(ActorRefs.Nobody))
        {
            return Array.Empty<PlannedExpenseView>();
        }
        try
        {
            var reply = await shard.Ask<PlannedList>(
                new ShardEnvelope(userId.ToString("N"), new Domain.Commands.Planned.ListPlanned(userId)),
                EveningAskTimeout).ConfigureAwait(false);
            return reply.Plans.Where(p => p.Date == date && p.Status == PlannedStatus.Active).ToArray();
        }
        catch (AskTimeoutException)
        {
            return Array.Empty<PlannedExpenseView>();
        }
        catch (NotSupportedException)
        {
            return Array.Empty<PlannedExpenseView>();
        }
    }

    private void OnEveningPromptPrepared(EveningPromptPrepared msg)
    {
        if (!_state.IsRegistered || _state.TelegramId is null)
        {
            return;
        }

        var text = BuildEveningPromptText(msg);
        Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.ChatId, text));

        var evt = new EveningQuestionAsked(_userId, msg.Date, msg.OccurredAt);
        Persist(evt, _ =>
        {
            MaybeSnapshot();
            ScheduleSilenceDeadline(msg.Settings.SilenceDeadlineHours);
            BecomeAwaitingDailyExpenses(msg);
        });
    }

    private void ScheduleSilenceDeadline(int hours)
    {
        _silenceDeadlineHandle?.Cancel();
        var delay = TimeSpan.FromHours(hours);
        _silenceDeadlineHandle = Context.System.Scheduler.ScheduleTellOnceCancelable(
            delay,
            Self,
            new SilenceDeadlineFired(_userId, DateTimeOffset.UtcNow + delay),
            ActorRefs.NoSender);
    }

    private void BecomeAwaitingDailyExpenses(EveningPromptPrepared msg)
    {
        var templates = msg.Templates;
        var settings = msg.Settings;

        BecomeStacked(() =>
        {
            Command<ReportExpense>(cmd =>
            {
                // Обрабатываем как обычно (используя HandleReportExpense), но после первой траты выходим из FSM.
                HandleReportExpense(cmd);
                ExitAwaitingDailyExpenses();
            });

            Command<SilenceDeadlineFired>(_ => OnSilenceDeadlineInFsm(templates, settings));

            Command<Cancel>(_ =>
            {
                Sender.Tell(new CancelAcknowledged(_userId));
                ExitAwaitingDailyExpenses();
            });

            // Дубликаты EveningTickFired в FSM игнорируем.
            Command<EveningTickFired>(_ => { });

            // Категоризация и snapshot — пропускаем.
            Command<CategorizeResponse>(HandleCategorizeResponse);
            Command<SaveSnapshotSuccess>(_ => { });
            Command<SaveSnapshotFailure>(failure => _log.Error(failure.Cause, "Snapshot save failed in FSM."));

            // Всё остальное — стэшим до выхода из state.
            CommandAny(_ => Stash.Stash());
        });
    }

    private void ExitAwaitingDailyExpenses()
    {
        _silenceDeadlineHandle?.Cancel();
        _silenceDeadlineHandle = null;
        UnbecomeStacked();
        Stash.UnstashAll();
    }

    private void OnSilenceDeadlineInFsm(IReadOnlyList<RecurringTemplateView> templates, UserScheduleSettings settings)
    {
        if (_state.TelegramId is not { } chatId)
        {
            ExitAwaitingDailyExpenses();
            return;
        }

        if (!settings.AutoConfirmOnSilence || templates.Count == 0)
        {
            Context.System.EventStream.Publish(new OutgoingTelegramReply(
                chatId, "Не дождался ответа на вечерний опрос — пропустил день. Можешь записать вручную."));
            ExitAwaitingDailyExpenses();
            return;
        }

        // Авто-фиксация: для каждого шаблона на сегодня — ExpenseReported + RecurringExpenseAutoConfirmed.
        var occurredAt = DateTimeOffset.UtcNow;
        var events = new List<IDomainEvent>(templates.Count * 2);
        var expenseIds = new List<(Guid expenseId, RecurringTemplateView t)>(templates.Count);
        foreach (var t in templates)
        {
            var expenseId = Guid.NewGuid();
            events.Add(new ExpenseReported(
                _userId, expenseId, _state.ActivePeriod?.PeriodId ?? Guid.Empty,
                t.Amount, occurredAt, t.Name, ExpenseSource.RecurringAuto));
            events.Add(new RecurringExpenseAutoConfirmed(_userId, t.TemplateId, expenseId, _activeEveningDate, occurredAt));
            expenseIds.Add((expenseId, t));
        }

        var summary = "Не дождался ответа — авто-фиксирую регулярные:\n"
            + string.Join("\n", expenseIds.Select(e => $"• {e.t.Name}: {e.t.Amount:0.00}"));

        var pending = events.Count;
        if (pending == 0)
        {
            ExitAwaitingDailyExpenses();
            return;
        }

        PersistAll(events, persisted =>
        {
            ApplyPersistedSilenceEvent(persisted);
            if (Interlocked.Decrement(ref pending) == 0)
            {
                MaybeSnapshot();
                Context.System.EventStream.Publish(new OutgoingTelegramReply(chatId, summary));
                ExitAwaitingDailyExpenses();
            }
        });
    }

    private void ApplyPersistedSilenceEvent(IDomainEvent evt)
    {
        if (evt is ExpenseReported e)
        {
            ApplyEvent(e);
            // Шаблон мог содержать Category — мапим напрямую без обращения к Categorizer.
            var category = Category.Other;
            var auto = new ExpenseCategorizedAutomatically(
                _userId, e.ExpenseId, category, ExpenseSource.RecurringAuto,
                NeedsReview: false,
                NormalizedDescription: NormalizedDescription.FromRaw(e.Description),
                OccurredAt: e.OccurredAt);
            Persist(auto, persisted =>
            {
                ApplyEvent(persisted);
                MaybeSnapshot();
            });
        }
    }

    private void OnSalaryDayTick(SalaryDayTickFired tick)
    {
        if (_state.TelegramId is not { } chatId)
        {
            return;
        }
        Context.System.EventStream.Publish(new OutgoingTelegramReply(
            chatId,
            $"Сегодня день зарплаты ({tick.SalaryDay}). Запиши доход через `/income <сумма>`."));
    }

    private TimeZoneInfo ResolveDefaultTimezone()
    {
        if (_state.Timezone is { Length: > 0 } tz)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch (TimeZoneNotFoundException) { /* ignore */ }
            catch (InvalidTimeZoneException) { /* ignore */ }
        }
        return TimeZoneInfo.Utc;
    }

    private static string BuildEveningPromptText(EveningPromptPrepared msg)
    {
        var sb = new System.Text.StringBuilder(256);
        sb.AppendLine($"Вечерний опрос ({msg.Date:yyyy-MM-dd}). Что потратил сегодня?");
        if (msg.Templates.Count > 0)
        {
            sb.AppendLine("Сегодня ожидаются регулярные:");
            foreach (var t in msg.Templates)
            {
                sb.AppendLine($"• {t.Name}: {t.Amount:0.00}");
            }
        }
        if (msg.Plans.Count > 0)
        {
            sb.AppendLine("Запланированные на сегодня:");
            foreach (var p in msg.Plans)
            {
                sb.AppendLine($"• {p.Description}: {p.Amount:0.00}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("Ответь свободным текстом, например `обед 750 + такси 400`. /cancel — пропустить.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Внутреннее сообщение: подготовленный список шаблонов/планов и снапшот настроек.</summary>
    private sealed record EveningPromptPrepared(
        DateOnly Date,
        long ChatId,
        IReadOnlyList<RecurringTemplateView> Templates,
        IReadOnlyList<PlannedExpenseView> Plans,
        UserScheduleSettings Settings,
        DateTimeOffset OccurredAt);
}
