using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Persistence;
using FinanceBot.Application.Actors.Advisor;
using FinanceBot.Application.Actors.Claude;
using FinanceBot.Application.Actors.Telegram.Messages;
using FinanceBot.Application.Actors.User.Messages;
using FinanceBot.Domain.Commands.User;
using FinanceBot.Domain.Events;
using FinanceBot.Domain.Events.Advisor;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Events.Scheduling;
using FinanceBot.Domain.Services;

namespace FinanceBot.Application.Actors.User;

/// <summary>
/// Per-user child actor: advisor pipeline (Claude + локальный fallback).
/// <see cref="PersistenceId"/> = <c>user-{userId:N}-advice</c>.
/// На <see cref="RequestConsultation"/> / Weekly/Monthly тики строит snapshot через
/// <see cref="AdvisorActor"/>, шлёт промпт в <see cref="ClaudeConsultantActor"/>.
/// При Unavailable + tick → persist <see cref="AdviceParked"/>, ждёт
/// <see cref="ClaudeBecameAvailable"/> на EventStream и повторяет.
/// При Unavailable + /advice → fallback на локальный совет.
/// </summary>
public sealed class UserAdviceActor : ReceivePersistentActor
{
    private readonly Guid _userId;
    private readonly ILoggingAdapter _log;
    private readonly IActorRef _parent;
    private readonly Dictionary<Guid, PendingAdvice> _pendingAdvice = new();
    private bool _adviceParkedAwaitingRecovery;
    private bool _resumeAskInFlight;
    private AdvisorTickType _parkedTickType;

    private static readonly TimeSpan ConversationTtl = TimeSpan.FromHours(1);
    private const int MaxAdviceConversationTurns = 5;
    private readonly List<AdviceConversationTurn> _conversation = new();
    private DateTimeOffset _lastInteractionUtc = DateTimeOffset.MinValue;
    private Dictionary<Guid, string> _recoveryBuffer = new();

    public override string PersistenceId { get; }

    public UserAdviceActor(Guid userId, IActorRef? parentRef = null)
    {
        _userId = userId;
        _log = Context.GetLogger();
        _parent = parentRef ?? Context.Parent;
        PersistenceId = $"user-{userId:N}-advice";

        Recover<ConsultationRequested>(evt =>
        {
            if (evt.Scope == AdvisorTickType.OnDemand && evt.UserQuestion is { } q)
                _recoveryBuffer[evt.CorrelationId] = q;
        });
        Recover<ConsultationAnswered>(evt =>
        {
            if (_recoveryBuffer.Remove(evt.CorrelationId, out var q))
            {
                _conversation.Add(new AdviceConversationTurn(q, evt.Response));
                if (_conversation.Count > MaxAdviceConversationTurns)
                    _conversation.RemoveRange(0, _conversation.Count - MaxAdviceConversationTurns);
                _lastInteractionUtc = evt.OccurredAt;
            }
        });
        Recover<RecoveryCompleted>(_ =>
        {
            _recoveryBuffer = new Dictionary<Guid, string>();
            if (_lastInteractionUtc != DateTimeOffset.MinValue
                && DateTimeOffset.UtcNow - _lastInteractionUtc > ConversationTtl)
            {
                _conversation.Clear();
            }
        });
        Recover<AdviceParked>(evt =>
        {
            _adviceParkedAwaitingRecovery = true;
            _parkedTickType = evt.TickType;
        });
        Recover<AdviceResumedWithFreshContext>(_ => _adviceParkedAwaitingRecovery = false);

        Context.System.EventStream.Subscribe(Self, typeof(ClaudeBecameAvailable));

        Command<GetAdviceConversation>(_ =>
            Sender.Tell(new AdviceConversationState(_conversation.AsReadOnly(), _lastInteractionUtc)));
        Command<EnrichedRequestConsultation>(OnRequestAdvice);
        Command<EnrichedWeeklyAdvisorTick>(t => StartAdvicePipeline(AdvisorTickType.Weekly, t.TelegramId, userQuestion: null));
        Command<EnrichedMonthlyAdvisorTick>(t => StartAdvicePipeline(AdvisorTickType.Monthly, t.TelegramId, userQuestion: null));
        Command<ClaudeBecameAvailable>(OnClaudeBecameAvailable);
        Command<UserSnapshotForResume>(OnUserSnapshotForResume);
        Command<Status.Failure>(OnSnapshotAskFailed);

        Command<BuildSnapshotResponse>(OnSnapshotResponse);
        Command<BuildLocalAdviceResponse>(OnLocalAdviceResponse);
        Command<ClaudeOkReply>(OnClaudeAdviceOk);
        Command<ClaudeUnavailableReply>(OnClaudeAdviceUnavailable);
    }

    private void OnRequestAdvice(EnrichedRequestConsultation msg)
    {
        if (string.Equals(msg.Request.Scope, "clear", StringComparison.OrdinalIgnoreCase))
        {
            _conversation.Clear();
            _lastInteractionUtc = DateTimeOffset.MinValue;
            return;
        }

        var tick = ResolveTickType(msg.Request.Scope);
        var question = string.IsNullOrWhiteSpace(msg.Request.Prompt) ? null : msg.Request.Prompt;

        if (tick == AdvisorTickType.OnDemand
            && _conversation.Count > 0
            && DateTimeOffset.UtcNow - _lastInteractionUtc > ConversationTtl)
        {
            _conversation.Clear();
        }

        StartAdvicePipeline(tick, msg.TelegramId, question);
    }

    private void OnClaudeBecameAvailable(ClaudeBecameAvailable _)
    {
        if (!_adviceParkedAwaitingRecovery || _resumeAskInFlight)
            return;

        _resumeAskInFlight = true;
        var tickType = _parkedTickType;
        _parent.Ask<UserSnapshot>(new GetUserSnapshot(_userId), TimeSpan.FromSeconds(5))
            .PipeTo(Self,
                success: snap => (object)new UserSnapshotForResume(snap, tickType),
                failure: ex => new Status.Failure(ex));
    }

    private void OnUserSnapshotForResume(UserSnapshotForResume msg)
    {
        if (!_adviceParkedAwaitingRecovery)
            return;

        _resumeAskInFlight = false;
        var evt = new AdviceResumedWithFreshContext(_userId, msg.TickType, DateTimeOffset.UtcNow);
        Persist(evt, _ =>
        {
            _adviceParkedAwaitingRecovery = false;
            StartAdvicePipeline(msg.TickType, msg.Snapshot.LastKnownChatId, userQuestion: null);
        });
    }

    private void OnSnapshotAskFailed(Status.Failure failure)
    {
        if (!_adviceParkedAwaitingRecovery)
            return;

        _resumeAskInFlight = false;
        _log.Warning("Failed to get user snapshot for parked advice resume: {0}", failure.Cause.Message);
        var tickType = _parkedTickType;
        var evt = new AdviceResumedWithFreshContext(_userId, tickType, DateTimeOffset.UtcNow);
        Persist(evt, _ =>
        {
            _adviceParkedAwaitingRecovery = false;
            StartAdvicePipeline(tickType, replyChatId: null, userQuestion: null);
        });
    }

    private void StartAdvicePipeline(AdvisorTickType tickType, long? replyChatId, string? userQuestion)
    {
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<AdvisorActorMarker>(out var advisor))
        {
            _log.Warning("AdvisorActor not registered; skipping advice for {0}.", _userId);
            return;
        }

        var corr = Guid.NewGuid();
        _pendingAdvice[corr] = new PendingAdvice(tickType, replyChatId, SnapshotForLocal: null, UserQuestion: userQuestion);
        advisor.Tell(new BuildSnapshotRequest(corr, _userId));
    }

    private void OnSnapshotResponse(BuildSnapshotResponse resp)
    {
        if (!_pendingAdvice.TryGetValue(resp.CorrelationId, out var ctxItem))
        {
            return;
        }
        if (resp.Snapshot is null)
        {
            _log.Warning("Snapshot build failed: {0}", resp.ErrorMessage);
            _pendingAdvice.Remove(resp.CorrelationId);
            ReplyToChat(ctxItem.ReplyChatId, "Не удалось собрать данные для совета. Попробуй позже.");
            return;
        }

        _pendingAdvice[resp.CorrelationId] = ctxItem with { SnapshotForLocal = resp.Snapshot };

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<ClaudeConsultantSingletonMarker>(out var claude))
        {
            FallbackToLocalAdvice(resp.CorrelationId, resp.Snapshot, ctxItem.TickType, ctxItem.ReplyChatId,
                persistAnswer: ctxItem.TickType == AdvisorTickType.OnDemand);
            return;
        }

        var history = ctxItem.TickType == AdvisorTickType.OnDemand && ctxItem.UserQuestion is not null
            ? _conversation
            : null;
        var userPrompt = AdvicePromptBuilder.Build(resp.Snapshot, ctxItem.TickType, ctxItem.UserQuestion, history);
        var requestedEvt = new ConsultationRequested(
            _userId, resp.CorrelationId, userPrompt, ctxItem.TickType, DateTimeOffset.UtcNow,
            UserQuestion: ctxItem.UserQuestion);

        Persist(requestedEvt, _ =>
        {
            var claudeReq = new ClaudeRequest(
                UseCase: ClaudeUseCase.Advice,
                SystemPrompt: AdviceSystemPrompts.AdviceSystem,
                UserPrompt: userPrompt,
                MaxTokens: 600,
                CorrelationId: resp.CorrelationId);
            claude.Tell(new ClaudeAskMessage(claudeReq));
        });
    }

    private void OnClaudeAdviceOk(ClaudeOkReply reply)
    {
        if (!_pendingAdvice.Remove(reply.CorrelationId, out var ctxItem))
        {
            return;
        }
        var content = string.IsNullOrWhiteSpace(reply.Content) ? "Совет пуст." : reply.Content.Trim();

        var evt = new ConsultationAnswered(
            _userId, reply.CorrelationId, content, ConsultationSource.Claude, DateTimeOffset.UtcNow);
        Persist(evt, persisted =>
        {
            AppendAdviceConversationTurn(ctxItem, persisted.Response);
            ReplyToChat(ctxItem.ReplyChatId, "💡 " + persisted.Response);
        });
    }

    private void OnClaudeAdviceUnavailable(ClaudeUnavailableReply reply)
    {
        if (!_pendingAdvice.Remove(reply.CorrelationId, out var ctxItem))
        {
            return;
        }

        if (ctxItem.TickType == AdvisorTickType.OnDemand)
        {
            if (ctxItem.SnapshotForLocal is { } snap)
            {
                FallbackToLocalAdvice(reply.CorrelationId, snap, ctxItem.TickType, ctxItem.ReplyChatId, persistAnswer: true);
            }
            else
            {
                ReplyToChat(ctxItem.ReplyChatId, "Claude недоступен и snapshot потерян. Попробуй позже.");
            }
            return;
        }

        var parked = new AdviceParked(_userId, ctxItem.TickType, DateTimeOffset.UtcNow);
        Persist(parked, persisted =>
        {
            _adviceParkedAwaitingRecovery = true;
            _parkedTickType = persisted.TickType;
        });
    }

    private void FallbackToLocalAdvice(Guid correlationId, AdvisorSnapshot snap, AdvisorTickType tickType, long? replyChatId, bool persistAnswer)
    {
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<AdvisorActorMarker>(out var advisor))
        {
            ReplyToChat(replyChatId, "Локальный советник недоступен.");
            return;
        }
        var userQuestion = _pendingAdvice.TryGetValue(correlationId, out var prev) ? prev.UserQuestion : null;
        _pendingAdvice[correlationId] = new PendingAdvice(tickType, replyChatId, snap, userQuestion)
        {
            ShouldPersistLocalAnswer = persistAnswer
        };
        advisor.Tell(new BuildLocalAdviceRequest(correlationId, _userId, snap, tickType));
    }

    private void OnLocalAdviceResponse(BuildLocalAdviceResponse resp)
    {
        if (!_pendingAdvice.Remove(resp.CorrelationId, out var ctxItem))
        {
            return;
        }
        var text = "(Claude недоступен, локальный совет)\n" + resp.Text;

        if (!ctxItem.ShouldPersistLocalAnswer)
        {
            AppendAdviceConversationTurn(ctxItem, resp.Text);
            ReplyToChat(ctxItem.ReplyChatId, text);
            return;
        }

        var evt = new ConsultationAnswered(
            _userId, resp.CorrelationId, resp.Text, ConsultationSource.LocalHeuristics, DateTimeOffset.UtcNow);
        Persist(evt, persisted =>
        {
            AppendAdviceConversationTurn(ctxItem, persisted.Response);
            ReplyToChat(ctxItem.ReplyChatId, text);
        });
    }

    private void AppendAdviceConversationTurn(PendingAdvice ctxItem, string answer)
    {
        if (ctxItem.TickType != AdvisorTickType.OnDemand || ctxItem.UserQuestion is null)
        {
            return;
        }
        _conversation.Add(new AdviceConversationTurn(ctxItem.UserQuestion, answer));
        if (_conversation.Count > MaxAdviceConversationTurns)
        {
            _conversation.RemoveRange(0, _conversation.Count - MaxAdviceConversationTurns);
        }
        _lastInteractionUtc = DateTimeOffset.UtcNow;
    }

    private void ReplyToChat(long? chatId, string text)
    {
        if (chatId is not { } id) return;
        Context.System.EventStream.Publish(new OutgoingTelegramReply(id, text));
    }

    private static AdvisorTickType ResolveTickType(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return AdvisorTickType.OnDemand;
        }
        return scope.Trim().ToLowerInvariant() switch
        {
            "week" or "weekly" => AdvisorTickType.Weekly,
            "month" or "monthly" => AdvisorTickType.Monthly,
            _ => AdvisorTickType.OnDemand
        };
    }

    public static Props CreateProps(Guid userId) => Props.Create(() => new UserAdviceActor(userId));

    private sealed record UserSnapshotForResume(UserSnapshot Snapshot, AdvisorTickType TickType);

    private sealed record PendingAdvice(
        AdvisorTickType TickType,
        long? ReplyChatId,
        AdvisorSnapshot? SnapshotForLocal,
        string? UserQuestion = null)
    {
        public bool ShouldPersistLocalAnswer { get; init; }
    }
}

public sealed record AdviceConversationTurn(string Question, string Answer);
public sealed record GetAdviceConversation(Guid UserId);
public sealed record AdviceConversationState(IReadOnlyList<AdviceConversationTurn> Turns, DateTimeOffset LastInteractionUtc);

public sealed record EnrichedRequestConsultation(RequestConsultation Request, long TelegramId);
public sealed record EnrichedWeeklyAdvisorTick(WeeklyAdvisorTickFired Tick, long TelegramId);
public sealed record EnrichedMonthlyAdvisorTick(MonthlyAdvisorTickFired Tick, long TelegramId);

internal static class AdviceSystemPrompts
{
    public const string AdviceSystem = """
        Ты — финансовый консультант. Дай краткий совет на основе данных пользователя.
        Ограничение: ответ до 1500 символов, одно цельное сообщение, без markdown-таблиц.
        """;
}
