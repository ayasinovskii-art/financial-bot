using System.Collections.Immutable;
using FinanceBot.Application.Actors.Scheduler;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Domain.Events.Wakeup;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Обработка WakeupCheck при детекте простоя SchedulerActor'ом.
/// Шлёт пользователю текст с пропущенными тиками; persist WakeupNotificationSent.
/// Дубликаты по тому же downtimeFrom отсекаем сравнением с last persisted событием.
/// </summary>
public sealed partial class UserActor
{
    private DateTimeOffset? _lastWakeupDowntimeFrom;

    partial void WireWakeupRecovery()
    {
        Recover<WakeupNotificationSent>(evt =>
        {
            // На фактическое восстановление: записываем "отметку" последнего downtimeFrom.
            // Используем OccurredAt как нижнюю границу — этот лог скорее для дубликат-фильтра.
            _lastWakeupDowntimeFrom = evt.OccurredAt;
        });

        Command<WakeupCheck>(OnWakeupCheck);
    }

    private void OnWakeupCheck(WakeupCheck check)
    {
        if (!_state.IsRegistered || _state.TelegramId is not { } chatId)
        {
            return;
        }

        // Дубликат-фильтр: если уже посылали уведомление позже downtimeFrom — пропускаем.
        if (_lastWakeupDowntimeFrom is { } last && last >= check.DowntimeFrom)
        {
            return;
        }

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

        Context.System.EventStream.Publish(new OutgoingTelegramReply(chatId, sb.ToString().TrimEnd()));

        var evt = new WakeupNotificationSent(
            _userId,
            check.MissedItems.ToImmutableArray(),
            DateTimeOffset.UtcNow);
        Persist(evt, persisted =>
        {
            _lastWakeupDowntimeFrom = check.DowntimeFrom;
            MaybeSnapshot();
            _ = persisted;
        });
    }
}
