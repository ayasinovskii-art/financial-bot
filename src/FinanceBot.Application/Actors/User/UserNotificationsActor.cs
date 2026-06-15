using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Domain.Events.Notifications;
using FinanceBot.Domain.Events.Scheduling;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Per-user child actor: хранит anti-spam счётчик отправок/сутки и шлёт проактивные уведомления.
/// PersistenceId = "user-{userId:N}-notifications" — отдельный журнал, не засоряет aggregate-root.
/// </summary>
public sealed class UserNotificationsActor : ReceivePersistentActor
{
    public const int DailyLimit = 3;

    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;
    private readonly Dictionary<DateOnly, int> _sentCounts = new();

    public override string PersistenceId { get; }

    public UserNotificationsActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();
        PersistenceId = $"user-{userId:N}-notifications";

        Recover<ProactiveNotificationSent>(evt =>
        {
            _sentCounts[evt.SentDate] = _sentCounts.GetValueOrDefault(evt.SentDate) + 1;
        });

        Command<EnrichedProactiveTrigger>(OnProactiveTrigger);
        Command<EnrichedWeeklyDigestTick>(OnWeeklyDigestTick);
        Command<SaveSnapshotSuccess>(_ => { });
        Command<SaveSnapshotFailure>(failure => _log.Error(failure.Cause, "UserNotificationsActor snapshot failed."));
    }

    private void OnProactiveTrigger(EnrichedProactiveTrigger msg)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.LocalDateTime);
        if (_sentCounts.GetValueOrDefault(today) >= DailyLimit)
        {
            _log.Debug("UserNotifications[{UserId}] daily limit reached for {Date}; skipping {TriggerKind}.",
                _userId, today, msg.TriggerKind);
            return;
        }

        var text = msg.TriggerKind switch
        {
            "large_expense" => BuildLargeExpenseText(msg),
            "bucket_near_limit" => BuildBucketNearLimitText(msg),
            _ => null
        };

        if (text is null)
        {
            return;
        }

        Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.TelegramId, text));

        var evt = new ProactiveNotificationSent(_userId, msg.TriggerKind, msg.BucketName, today, DateTimeOffset.UtcNow);
        Persist(evt, persisted => _sentCounts[persisted.SentDate] = _sentCounts.GetValueOrDefault(persisted.SentDate) + 1);
    }

    private void OnWeeklyDigestTick(EnrichedWeeklyDigestTick msg)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.LocalDateTime);
        if (_sentCounts.GetValueOrDefault(today) >= DailyLimit)
        {
            _log.Debug("UserNotifications[{UserId}] daily limit reached for {Date}; skipping weekly digest.",
                _userId, today);
            return;
        }

        var text = BuildWeeklyDigestText();
        Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.TelegramId, text));

        var evt = new ProactiveNotificationSent(_userId, "weekly_digest", string.Empty, today, DateTimeOffset.UtcNow);
        Persist(evt, persisted => _sentCounts[persisted.SentDate] = _sentCounts.GetValueOrDefault(persisted.SentDate) + 1);
    }

    private static string BuildLargeExpenseText(EnrichedProactiveTrigger msg)
    {
        var pct = msg.AllocationForBucket > 0
            ? (int)Math.Round(msg.Amount / msg.AllocationForBucket * 100m)
            : 0;
        return $"💸 Крупная трата {msg.Amount:0.##} — это {pct}% бюджета {msg.BucketName} за один раз.";
    }

    private static string BuildBucketNearLimitText(EnrichedProactiveTrigger msg)
    {
        var pct = msg.AllocationForBucket > 0
            ? (int)Math.Round(msg.Amount / msg.AllocationForBucket * 100m)
            : 0;
        return $"⚠️ Бюджет {msg.BucketName} использован на {pct}%. Период ещё не закончился — следи за расходами.";
    }

    private static string BuildWeeklyDigestText()
        => "📊 Неделя завершена. Посмотри итоги через /stats.";

    public static Props CreateProps(Guid userId)
        => Props.Create(() => new UserNotificationsActor(userId));
}

/// <summary>Обёртка над проактивным триггером — parent добавляет TelegramId.</summary>
public sealed record EnrichedProactiveTrigger(
    string TriggerKind,
    string BucketName,
    decimal Amount,
    decimal AllocationForBucket,
    long TelegramId);

/// <summary>Обёртка над WeeklyDigestTickFired — parent добавляет TelegramId.</summary>
public sealed record EnrichedWeeklyDigestTick(WeeklyDigestTickFired Tick, long TelegramId);
