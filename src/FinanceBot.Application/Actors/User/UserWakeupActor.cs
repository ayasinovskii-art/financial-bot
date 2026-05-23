using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using FinanceBot.Application.Actors.Scheduler;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Domain.Events.Wakeup;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Per-user child actor: обработка <see cref="WakeupCheck"/> с дедупликацией по downtimeFrom.
/// Свой <see cref="PersistenceId"/> = <c>user-{userId:N}-wakeup</c> — события не перемешиваются
/// с aggregate-root журналом <see cref="UserActor"/>.
/// </summary>
public sealed class UserWakeupActor : ReceivePersistentActor
{
    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;
    private DateTimeOffset? _lastDowntimeFrom;

    public override string PersistenceId { get; }

    public UserWakeupActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();
        PersistenceId = $"user-{userId:N}-wakeup";

        Recover<WakeupNotificationSent>(evt => _lastDowntimeFrom = evt.OccurredAt);

        Command<EnrichedWakeupCheck>(OnCheck);
    }

    private void OnCheck(EnrichedWakeupCheck msg)
    {
        var check = msg.Check;
        if (_lastDowntimeFrom is { } last && last >= check.DowntimeFrom)
        {
            return;
        }

        var text = BuildText(check);
        Context.System.EventStream.Publish(new OutgoingTelegramReply(msg.TelegramId, text));

        var evt = new WakeupNotificationSent(
            _userId,
            check.MissedItems.ToImmutableArray(),
            DateTimeOffset.UtcNow);
        Persist(evt, _ => _lastDowntimeFrom = check.DowntimeFrom);
    }

    private static string BuildText(WakeupCheck check)
    {
        var sb = new System.Text.StringBuilder(256);
        sb.AppendLine("🔔 Я был недоступен.");
        sb.AppendLine($"Простой: {check.DowntimeFrom:yyyy-MM-dd HH:mm} → {check.DowntimeTo:yyyy-MM-dd HH:mm} UTC.");
        if (check.MissedItems.Count == 0)
        {
            sb.AppendLine("Пропущенных тиков нет.");
        }
        else
        {
            sb.AppendLine("Пропущенное:");
            foreach (var item in check.MissedItems)
            {
                sb.AppendLine($"• {item}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("Если нужно — запиши доход/траты вручную через /income, /expense.");
        return sb.ToString().TrimEnd();
    }

    public static Props CreateProps(Guid userId)
        => Props.Create(() => new UserWakeupActor(userId));
}

/// <summary>Обёртка над <see cref="WakeupCheck"/>: parent добавляет TelegramId, который child использует для reply.</summary>
public sealed record EnrichedWakeupCheck(WakeupCheck Check, long TelegramId);
