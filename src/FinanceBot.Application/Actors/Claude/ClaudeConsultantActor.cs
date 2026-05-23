using Akka.Actor;
using Akka.Event;
using FinanceBot.Application.Actors.Common;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Services;
using Microsoft.Extensions.Options;

namespace FinanceBot.Application.Actors.Claude;

/// <summary>
/// Cluster singleton, оборачивающий <see cref="IClaudeClient"/> с in-memory state machine
/// Available / Unavailable(until, reason). См. ТЗ §7.2.
///
/// Без Polly CB и без retry — ручной FSM.
/// </summary>
public sealed class ClaudeConsultantActor : ReceiveActor
{
    private readonly IClaudeClient _client;
    private readonly ClaudeConsultantOptions _options;
    private readonly ILoggingAdapter _log;
    private readonly SemaphoreSlim _concurrency;
    private readonly int _maxQueueDepth;
    private int _inFlight;

    private bool _available = true;
    private DateTimeOffset _unavailableUntil = DateTimeOffset.MinValue;
    private ClaudeUnavailabilityReason _reason = ClaudeUnavailabilityReason.None;

    public ClaudeConsultantActor(IClaudeClient client, IOptions<ClaudeConsultantOptions> options)
    {
        _client = client;
        _options = options.Value;
        _log = Context.GetLogger();
        _concurrency = new SemaphoreSlim(_options.ConcurrencyLimit, _options.ConcurrencyLimit);
        // Back-pressure cap: одновременно «принято к обработке» (в работе + в очереди семафора).
        _maxQueueDepth = _options.ConcurrencyLimit * 4;

        Receive<Ping>(_ => Sender.Tell(new Pong(Self.Path.ToStringWithoutAddress())));

        Receive<ClaudeAskMessage>(HandleAsk);
        Receive<InternalResponse>(HandleInternalResponse);
        Receive<ResetUnavailable>(_ => SetAvailable(reason: "manual reset"));
        Receive<ClaudeBecameAvailable>(evt => SetAvailable("auto-recovery"));
    }

    private void HandleAsk(ClaudeAskMessage msg)
    {
        var sender = Sender;
        var now = DateTimeOffset.UtcNow;

        if (!_available && now < _unavailableUntil)
        {
            sender.Tell(new ClaudeUnavailableReply(msg.Request.CorrelationId, _reason, _unavailableUntil));
            return;
        }

        if (_inFlight >= _maxQueueDepth)
        {
            _log.Warning("ClaudeConsultantActor overloaded ({InFlight}/{Cap}); rejecting corr={Corr}.",
                _inFlight, _maxQueueDepth, msg.Request.CorrelationId);
            sender.Tell(new ClaudeUnavailableReply(
                msg.Request.CorrelationId, ClaudeUnavailabilityReason.Overloaded, now.AddSeconds(30)));
            return;
        }

        _inFlight++;
        _ = ExecuteAsync(msg.Request, sender);
    }

    private async Task ExecuteAsync(ClaudeRequest request, IActorRef sender)
    {
        await _concurrency.WaitAsync().ConfigureAwait(false);
        var self = Self;
        try
        {
            var response = await _client.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
            self.Tell(new InternalResponse(request.CorrelationId, response, sender));
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private void HandleInternalResponse(InternalResponse msg)
    {
        if (_inFlight > 0)
        {
            _inFlight--;
        }
        if (msg.Response.IsSuccess)
        {
            if (!_available)
            {
                SetAvailable("successful response after recovery");
            }
            msg.OriginalSender.Tell(new ClaudeOkReply(
                msg.CorrelationId, msg.Response.Content ?? string.Empty,
                msg.Response.LatencyMs, msg.Response.TokensRemaining, msg.Response.TokensResetAt));
            return;
        }

        var reason = msg.Response.FailureReason ?? ClaudeUnavailabilityReason.Other;
        var until = ResolveUnavailableUntil(reason, msg.Response.TokensResetAt);
        SetUnavailable(reason, until);
        msg.OriginalSender.Tell(new ClaudeUnavailableReply(msg.CorrelationId, reason, until));
    }

    private DateTimeOffset ResolveUnavailableUntil(ClaudeUnavailabilityReason reason, DateTimeOffset? resetAt)
    {
        var now = DateTimeOffset.UtcNow;
        return reason switch
        {
            ClaudeUnavailabilityReason.RateLimited or ClaudeUnavailabilityReason.TokensExhausted
                => resetAt ?? now.AddMinutes(5),
            ClaudeUnavailabilityReason.TransientError or ClaudeUnavailabilityReason.Timeout
                => NextLocalHour(now, _options.TransientUnavailableUntilHour),
            _ => now.AddMinutes(5)
        };
    }

    private static DateTimeOffset NextLocalHour(DateTimeOffset now, int hour)
    {
        var local = now.ToLocalTime();
        var target = new DateTimeOffset(local.Year, local.Month, local.Day, hour, 0, 0, local.Offset);
        if (target <= local)
        {
            target = target.AddDays(1);
        }
        return target.ToUniversalTime();
    }

    private void SetAvailable(string reason)
    {
        if (_available)
        {
            return;
        }
        _available = true;
        _reason = ClaudeUnavailabilityReason.None;
        _unavailableUntil = DateTimeOffset.MinValue;
        _log.Info("ClaudeConsultantActor → Available ({Reason}).", reason);
        Context.System.EventStream.Publish(new ClaudeBecameAvailable(DateTimeOffset.UtcNow));
    }

    private void SetUnavailable(ClaudeUnavailabilityReason reason, DateTimeOffset until)
    {
        if (_available || reason != _reason || _unavailableUntil != until)
        {
            _log.Warning("ClaudeConsultantActor → Unavailable(reason={Reason}, until={Until}).", reason, until);
            Context.System.EventStream.Publish(new ClaudeBecameUnavailable(reason, until, DateTimeOffset.UtcNow));
        }
        _available = false;
        _reason = reason;
        _unavailableUntil = until;
    }

    // InternalResponse handler регистрируется в конструкторе.

    public static Props CreateProps(IClaudeClient client, IOptions<ClaudeConsultantOptions> options)
        => Props.Create(() => new ClaudeConsultantActor(client, options));

    private sealed record InternalResponse(Guid CorrelationId, ClaudeResponse Response, IActorRef OriginalSender);
}

/// <summary>Опции для ClaudeConsultantActor (вычитываются из той же Claude-секции).</summary>
public sealed class ClaudeConsultantOptions
{
    public int ConcurrencyLimit { get; init; } = 3;
    public int TransientUnavailableUntilHour { get; init; } = 20;
}

/// <summary>Команда: «спросить Claude».</summary>
public sealed record ClaudeAskMessage(ClaudeRequest Request);

/// <summary>Reply: успешный ответ.</summary>
public sealed record ClaudeOkReply(
    Guid CorrelationId,
    string Content,
    long LatencyMs,
    int? TokensRemaining,
    DateTimeOffset? TokensResetAt);

/// <summary>Reply: Claude недоступен.</summary>
public sealed record ClaudeUnavailableReply(
    Guid CorrelationId,
    ClaudeUnavailabilityReason Reason,
    DateTimeOffset UnavailableUntil);

/// <summary>Команда: ручной сброс state machine в Available (используется ClaudeAutoRecoveryTick).</summary>
public sealed record ResetUnavailable;

/// <summary>Marker-тип для регистрации singleton'а в registry.</summary>
public sealed class ClaudeConsultantSingletonMarker;
