using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.Claude;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Application.Configuration;
using FinanceBot.Application.Projections;
using FinanceBot.Application.Scheduling;
using FinanceBot.Domain.Events.Scheduling;
using FinanceBot.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace FinanceBot.Application.Actors.Scheduler;

/// <summary>
/// Cluster singleton: пишет SystemHeartbeat и шлёт per-user тики (Stage 17+).
/// Архитектура — поллинг каждую минуту: для каждого юзера вычисляются тики, которые должны были сработать
/// в окне (lastCheckAt, now], и отправляются в shard region User.
/// На старте — детект простоя по heartbeat-gap (Stage 18) и emit WakeupCheck.
/// </summary>
public sealed class SchedulerActor : ReceiveActor, IWithTimers
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    /// <summary>Если разрыв в heartbeat больше — считаем простоем и шлём wakeup.</summary>
    public static readonly TimeSpan DowntimeThreshold = TimeSpan.FromMinutes(5);
    private const string TickKey = "system-tick";

    private readonly ISystemHeartbeatWriter _heartbeatWriter;
    private readonly IUserDirectory _directory;
    private readonly IUserScheduleResolver _resolver;
    private readonly TimeZoneInfo _serverTimezone;
    private readonly ILoggingAdapter _log;

    private DateTimeOffset _lastCheckAt;
    private bool _startupCheckDone;
    private bool _tickInFlight;

    public ITimerScheduler Timers { get; set; } = null!;

    public SchedulerActor(
        ISystemHeartbeatWriter heartbeatWriter,
        IUserDirectory directory,
        IUserScheduleResolver resolver,
        IOptions<SchedulerOptions>? options = null)
    {
        _heartbeatWriter = heartbeatWriter;
        _directory = directory;
        _resolver = resolver;
        _log = Context.GetLogger();
        var tzId = options?.Value.ServerTimezone ?? "Europe/Moscow";
        try
        {
            _serverTimezone = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch (TimeZoneNotFoundException)
        {
            _log.Warning("Scheduler:ServerTimezone='{Tz}' not found; falling back to TimeZoneInfo.Local.", tzId);
            _serverTimezone = TimeZoneInfo.Local;
        }

        Receive<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));
        Receive<SystemTick>(tick => OnSystemTick(tick));
        Receive<TickCheckCompleted>(msg => OnTickCheckCompleted(msg));
        Receive<StartupCheckCompleted>(msg => OnStartupCheckCompleted(msg));

        ReceiveAny(msg => _log.Debug("SchedulerActor unhandled {MessageType}", msg.GetType().Name));
    }

    protected override void PreStart()
    {
        // Первый тик через 5 секунд (даём другим компонентам подняться), потом каждую минуту.
        Timers.StartPeriodicTimer(
            key: TickKey,
            msg: new SystemTick(),
            initialDelay: TimeSpan.FromSeconds(5),
            interval: TickInterval);
        _lastCheckAt = DateTimeOffset.UtcNow;
        base.PreStart();
    }

    private void OnSystemTick(SystemTick tick)
    {
        var now = DateTimeOffset.UtcNow;
        _ = tick;
        _ = WriteHeartbeatAsync(now);

        if (_tickInFlight)
        {
            _log.Debug("Previous tick check still running; skipping at {Now}.", now);
            return;
        }

        _tickInFlight = true;
        if (!_startupCheckDone)
        {
            _ = RunStartupCheckAsync(now);
            return;
        }

        _ = RunPeriodicTickCheckAsync(_lastCheckAt, now);
    }

    private async Task WriteHeartbeatAsync(DateTimeOffset now)
    {
        try
        {
            await _heartbeatWriter.UpsertAsync(now, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to write system heartbeat.");
        }
    }

    private async Task RunStartupCheckAsync(DateTimeOffset now)
    {
        var self = Self;
        try
        {
            var lastSeen = await _heartbeatWriter.ReadLastSeenAsync(CancellationToken.None).ConfigureAwait(false);
            var users = await _directory.ListAsync(CancellationToken.None).ConfigureAwait(false);

            var downtimeFrom = lastSeen;
            var downtimeTo = now;
            var hasDowntime = lastSeen.HasValue && (now - lastSeen.Value) > DowntimeThreshold;

            if (hasDowntime)
            {
                _log.Info("System downtime detected: {From} → {To} ({Seconds}s).",
                    downtimeFrom, downtimeTo, (downtimeTo - downtimeFrom!.Value).TotalSeconds);

                if (!ActorRegistry.For(Context.System).TryGet<UserShardMarker>(out var userShard))
                {
                    _log.Warning("UserShardMarker not registered; skipping wakeup notifications.");
                }
                else
                {
                    foreach (var user in users)
                    {
                        var missed = await CollectMissedItemsAsync(
                            user.Settings, downtimeFrom!.Value, downtimeTo, CancellationToken.None).ConfigureAwait(false);
                        var check = new WakeupCheck(user.UserId, downtimeFrom.Value, downtimeTo, missed);
                        userShard.Tell(new ShardEnvelope(user.UserId.ToString("N"), check));
                    }
                }
            }

            self.Tell(new StartupCheckCompleted(now));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Startup check failed.");
            self.Tell(new StartupCheckCompleted(now));
        }
    }

    private async Task RunPeriodicTickCheckAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var self = Self;
        try
        {
            var registry = ActorRegistry.For(Context.System);

            // System-wide: ClaudeAutoRecoveryTick на 20:00 server time (см. §3.11, §10.16).
            DispatchClaudeAutoRecoveryIfDue(registry, from, to);

            if (!registry.TryGet<UserShardMarker>(out var userShard))
            {
                self.Tell(new TickCheckCompleted(to));
                return;
            }

            var users = await _directory.ListAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var user in users)
            {
                await DispatchDueTicksAsync(user, from, to, userShard, CancellationToken.None).ConfigureAwait(false);
            }
            self.Tell(new TickCheckCompleted(to));
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Periodic tick check failed.");
            self.Tell(new TickCheckCompleted(to));
        }
    }

    private void DispatchClaudeAutoRecoveryIfDue(ActorRegistry registry, DateTimeOffset from, DateTimeOffset to)
    {
        if (!ServerTimeWindow.ContainsLocalTimeOfDay(from, to,
            ClaudeAutoRecoveryHour, minute: 0, _serverTimezone))
        {
            return;
        }
        if (!registry.TryGet<ClaudeConsultantSingletonMarker>(out var claude))
        {
            return;
        }
        _log.Info("ClaudeAutoRecoveryTick fired (server 20:00) → ResetUnavailable.");
        claude.Tell(new ResetUnavailable());
    }

    /// <summary>Час локального времени сервера, когда шлём ClaudeAutoRecoveryTick (default 20).</summary>
    public const int ClaudeAutoRecoveryHour = 20;

    /// <summary>Час local-time пользователя, когда шлём WeeklyDigestTick по воскресеньям.</summary>
    public const int WeeklyDigestHour = 9;

    private async Task DispatchDueTicksAsync(
        UserDirectoryEntry user, DateTimeOffset from, DateTimeOffset to, IActorRef userShard, CancellationToken ct)
    {
        // EveningTick: проверяем, было ли evening_time в окне (from, to].
        if (CrossesLocalTime(user.Settings.Timezone, from, to, user.Settings.EveningTime))
        {
            userShard.Tell(new ShardEnvelope(
                user.UserId.ToString("N"),
                new EveningTickFired(user.UserId, to)));
        }

        // SalaryDayTick: enumerate в окне.
        await foreach (var ts in _resolver.EnumerateSalaryDayTicksAsync(user.Settings, from, to, ct).ConfigureAwait(false))
        {
            // Найти "salary day" (число, на которое смотрим) — берём день в TZ юзера.
            var local = TimeZoneInfo.ConvertTime(ts, user.Settings.Timezone);
            userShard.Tell(new ShardEnvelope(
                user.UserId.ToString("N"),
                new SalaryDayTickFired(user.UserId, local.Day, ts)));
        }

        // Weekly advisor (понедельник first workday 09:00).
        if (await IsAdvisorTickInWindowAsync(user.Settings, from, to, weekly: true, ct).ConfigureAwait(false))
        {
            userShard.Tell(new ShardEnvelope(
                user.UserId.ToString("N"),
                new WeeklyAdvisorTickFired(user.UserId, to)));
        }

        // Monthly advisor (1-е число first workday 09:00).
        if (await IsAdvisorTickInWindowAsync(user.Settings, from, to, weekly: false, ct).ConfigureAwait(false))
        {
            userShard.Tell(new ShardEnvelope(
                user.UserId.ToString("N"),
                new MonthlyAdvisorTickFired(user.UserId, to)));
        }

        // WeeklyDigestTick: воскресенье в 09:00 local-time пользователя.
        if (CrossesLocalSunday(user.Settings.Timezone, from, to, WeeklyDigestHour))
        {
            userShard.Tell(new ShardEnvelope(
                user.UserId.ToString("N"),
                new WeeklyDigestTickFired(user.UserId, to)));
        }
    }

    private async Task<bool> IsAdvisorTickInWindowAsync(
        UserScheduleSettings s, DateTimeOffset from, DateTimeOffset to, bool weekly, CancellationToken ct)
    {
        // Чтобы не задваивать с NextXxxAdvisorTickAsync (которые отдают будущее), считаем «прошлое»:
        // сдвигаем окно «назад» на 1 минуту и спрашиваем next-from-from. Если оно < to — попало в окно.
        var probe = from.AddTicks(-1);
        var next = weekly
            ? await _resolver.NextWeeklyAdvisorTickAsync(s, probe, ct).ConfigureAwait(false)
            : await _resolver.NextMonthlyAdvisorTickAsync(s, probe, ct).ConfigureAwait(false);
        return next > from && next <= to;
    }

    private static bool CrossesLocalSunday(TimeZoneInfo tz, DateTimeOffset from, DateTimeOffset to, int hour)
    {
        var localFrom = TimeZoneInfo.ConvertTime(from, tz);
        var localTo = TimeZoneInfo.ConvertTime(to, tz);
        for (var d = localFrom.Date; d <= localTo.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Sunday)
            {
                continue;
            }
            var local = new DateTime(d.Year, d.Month, d.Day, hour, 0, 0, DateTimeKind.Unspecified);
            var fireAt = new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
            if (fireAt > from && fireAt <= to)
            {
                return true;
            }
        }
        return false;
    }

    private static bool CrossesLocalTime(TimeZoneInfo tz, DateTimeOffset from, DateTimeOffset to, TimeOfDay time)
    {
        // Проверяем все локальные дни в окне. Окно может затронуть 1–3 дня (TZ-сдвиги).
        var localFrom = TimeZoneInfo.ConvertTime(from, tz);
        var localTo = TimeZoneInfo.ConvertTime(to, tz);
        for (var d = localFrom.Date; d <= localTo.Date; d = d.AddDays(1))
        {
            var local = new DateTime(d.Year, d.Month, d.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
            var fireAt = new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
            if (fireAt > from && fireAt <= to)
            {
                return true;
            }
        }
        return false;
    }

    private async Task<IReadOnlyList<string>> CollectMissedItemsAsync(
        UserScheduleSettings s, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var items = new List<string>(8);

        // Evening ticks по дням.
        var localFrom = TimeZoneInfo.ConvertTime(from, s.Timezone);
        var localTo = TimeZoneInfo.ConvertTime(to, s.Timezone);
        for (var d = localFrom.Date; d <= localTo.Date; d = d.AddDays(1))
        {
            var local = new DateTime(d.Year, d.Month, d.Day, s.EveningTime.Hour, s.EveningTime.Minute, 0, DateTimeKind.Unspecified);
            var fireAt = new DateTimeOffset(local, s.Timezone.GetUtcOffset(local)).ToUniversalTime();
            if (fireAt > from && fireAt <= to)
            {
                items.Add($"вечерний опрос {d:yyyy-MM-dd}");
            }
        }

        // Salary ticks.
        await foreach (var ts in _resolver.EnumerateSalaryDayTicksAsync(s, from, to, ct).ConfigureAwait(false))
        {
            var local = TimeZoneInfo.ConvertTime(ts, s.Timezone);
            items.Add($"зарплата {local:yyyy-MM-dd}");
        }

        if (await IsAdvisorTickInWindowAsync(s, from, to, weekly: true, ct).ConfigureAwait(false))
        {
            items.Add("еженедельный совет (запросить через /advice week)");
        }
        if (await IsAdvisorTickInWindowAsync(s, from, to, weekly: false, ct).ConfigureAwait(false))
        {
            items.Add("ежемесячный совет (запросить через /advice month)");
        }

        return items;
    }

    private void OnTickCheckCompleted(TickCheckCompleted msg)
    {
        _lastCheckAt = msg.At;
        _tickInFlight = false;
    }

    private void OnStartupCheckCompleted(StartupCheckCompleted msg)
    {
        _startupCheckDone = true;
        _lastCheckAt = msg.At;
        _tickInFlight = false;
    }

    public static Props CreateProps(
        ISystemHeartbeatWriter writer,
        IUserDirectory directory,
        IUserScheduleResolver resolver,
        IOptions<SchedulerOptions>? options = null)
        => Props.Create(() => new SchedulerActor(writer, directory, resolver, options));

    private sealed record SystemTick;
    private sealed record TickCheckCompleted(DateTimeOffset At);
    private sealed record StartupCheckCompleted(DateTimeOffset At);
}

/// <summary>
/// Сообщение от SchedulerActor → UserActor: «обнаружен простой, проверь пропущенное».
/// </summary>
public sealed record WakeupCheck(
    Guid UserId,
    DateTimeOffset DowntimeFrom,
    DateTimeOffset DowntimeTo,
    IReadOnlyList<string> MissedItems);
