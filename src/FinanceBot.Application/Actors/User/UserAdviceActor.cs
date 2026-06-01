using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Persistence;
using FinanceBot.Application.Actors.Advisor;
using FinanceBot.Application.Actors.Claude;
using FinanceBot.Application.Actors.Telegram.Messages;
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
    private readonly Dictionary<Guid, PendingAdvice> _pendingAdvice = new();
    private bool _adviceParkedAwaitingRecovery;
    private AdvisorTickType _parkedTickType;

    public override string PersistenceId { get; }

    public UserAdviceActor(Guid userId)
    {
        _userId = userId;
        _log = Context.GetLogger();
        PersistenceId = $"user-{userId:N}-advice";

        Recover<ConsultationRequested>(_ => { });
        Recover<ConsultationAnswered>(_ => { });
        Recover<AdviceParked>(evt =>
        {
            _adviceParkedAwaitingRecovery = true;
            _parkedTickType = evt.TickType;
        });
        Recover<AdviceResumedWithFreshContext>(_ => _adviceParkedAwaitingRecovery = false);

        Context.System.EventStream.Subscribe(Self, typeof(ClaudeBecameAvailable));

        Command<EnrichedRequestConsultation>(OnRequestAdvice);
        Command<EnrichedWeeklyAdvisorTick>(t => StartAdvicePipeline(AdvisorTickType.Weekly, t.TelegramId, userQuestion: null));
        Command<EnrichedMonthlyAdvisorTick>(t => StartAdvicePipeline(AdvisorTickType.Monthly, t.TelegramId, userQuestion: null));
        Command<ClaudeBecameAvailable>(OnClaudeBecameAvailable);

        Command<BuildSnapshotResponse>(OnSnapshotResponse);
        Command<BuildLocalAdviceResponse>(OnLocalAdviceResponse);
        Command<ClaudeOkReply>(OnClaudeAdviceOk);
        Command<ClaudeUnavailableReply>(OnClaudeAdviceUnavailable);
    }

    private void OnRequestAdvice(EnrichedRequestConsultation msg)
    {
        var tick = ResolveTickType(msg.Request.Scope);
        var question = string.IsNullOrWhiteSpace(msg.Request.Prompt) ? null : msg.Request.Prompt;
        StartAdvicePipeline(tick, msg.TelegramId, question);
    }

    private void OnClaudeBecameAvailable(ClaudeBecameAvailable _)
    {
        if (!_adviceParkedAwaitingRecovery)
        {
            return;
        }
        var evt = new AdviceResumedWithFreshContext(_userId, _parkedTickType, DateTimeOffset.UtcNow);
        var tickType = _parkedTickType;
        Persist(evt, _ =>
        {
            _adviceParkedAwaitingRecovery = false;
            // На park'нутый tick reply-chat неизвестен — публикуем без явного TelegramId
            // если кто-то подпишется через scheduler. Для надёжности этот путь нужно
            // расширить хранением chatId в parked-state (TODO).
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

        var userPrompt = AdvicePromptBuilder.Build(resp.Snapshot, ctxItem.TickType, ctxItem.UserQuestion);
        var requestedEvt = new ConsultationRequested(
            _userId, resp.CorrelationId, userPrompt, ctxItem.TickType, DateTimeOffset.UtcNow);

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
        Persist(evt, persisted => ReplyToChat(ctxItem.ReplyChatId, "💡 " + persisted.Response));
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
            ReplyToChat(ctxItem.ReplyChatId, text);
            return;
        }

        var evt = new ConsultationAnswered(
            _userId, resp.CorrelationId, resp.Text, ConsultationSource.LocalHeuristics, DateTimeOffset.UtcNow);
        Persist(evt, _ => ReplyToChat(ctxItem.ReplyChatId, text));
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

    private sealed record PendingAdvice(
        AdvisorTickType TickType,
        long? ReplyChatId,
        AdvisorSnapshot? SnapshotForLocal,
        string? UserQuestion = null)
    {
        public bool ShouldPersistLocalAnswer { get; init; }
    }
}

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
