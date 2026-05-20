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
/// Stage 19: advisor pipeline. На /advice или WeeklyAdvisorTickFired/MonthlyAdvisorTickFired —
/// строим snapshot через AdvisorActor, отправляем prompt в ClaudeConsultantActor, отвечаем пользователю.
/// При Unavailable + tick → persist AdviceParked, ждём ClaudeBecameAvailable на EventStream и повторяем.
/// При Unavailable + /advice → fallback на локальный совет от AdvisorActor.
/// </summary>
public sealed partial class UserActor
{
    private static readonly TimeSpan AdvisorAskTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<Guid, PendingAdvice> _pendingAdvice = new();
    private bool _adviceParkedAwaitingRecovery;
    private AdvisorTickType _parkedTickType;

    partial void WireStage19()
    {
        Recover<ConsultationRequested>(_ => { /* informational */ });
        Recover<ConsultationAnswered>(_ => { /* informational */ });
        Recover<AdviceParked>(evt =>
        {
            _adviceParkedAwaitingRecovery = true;
            _parkedTickType = evt.TickType;
        });
        Recover<AdviceResumedWithFreshContext>(_ =>
        {
            _adviceParkedAwaitingRecovery = false;
        });

        Context.System.EventStream.Subscribe(Self, typeof(ClaudeBecameAvailable));

        Command<RequestConsultation>(OnRequestAdvice);
        Command<WeeklyAdvisorTickFired>(OnWeeklyAdvisorTick);
        Command<MonthlyAdvisorTickFired>(OnMonthlyAdvisorTick);
        Command<ClaudeBecameAvailable>(OnClaudeBecameAvailable);

        Command<BuildSnapshotResponse>(OnSnapshotResponse);
        Command<BuildLocalAdviceResponse>(OnLocalAdviceResponse);
        Command<ClaudeOkReply>(OnClaudeAdviceOk);
        Command<ClaudeUnavailableReply>(OnClaudeAdviceUnavailable);
    }

    private void OnRequestAdvice(RequestConsultation cmd)
    {
        if (!_state.IsRegistered)
        {
            ReplyOrIgnore("Сначала зарегистрируйся через /start.");
            return;
        }
        var tick = ResolveTickType(cmd.Scope);
        StartAdvicePipeline(tick, replyChatId: _state.TelegramId);
    }

    private void OnWeeklyAdvisorTick(WeeklyAdvisorTickFired _)
        => StartAdvicePipeline(AdvisorTickType.Weekly, replyChatId: _state.TelegramId);

    private void OnMonthlyAdvisorTick(MonthlyAdvisorTickFired _)
        => StartAdvicePipeline(AdvisorTickType.Monthly, replyChatId: _state.TelegramId);

    private void OnClaudeBecameAvailable(ClaudeBecameAvailable _)
    {
        if (!_adviceParkedAwaitingRecovery)
        {
            return;
        }
        var evt = new AdviceResumedWithFreshContext(_userId, _parkedTickType, DateTimeOffset.UtcNow);
        var tickType = _parkedTickType;
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            _adviceParkedAwaitingRecovery = false;
            StartAdvicePipeline(tickType, replyChatId: _state.TelegramId);
        });
    }

    private void StartAdvicePipeline(AdvisorTickType tickType, long? replyChatId)
    {
        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<AdvisorActorMarker>(out var advisor))
        {
            _log.Warning("AdvisorActor not registered; skipping advice for {UserId}.", _userId);
            return;
        }

        var corr = Guid.NewGuid();
        _pendingAdvice[corr] = new PendingAdvice(tickType, replyChatId, SnapshotForLocal: null);
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
            _log.Warning("Snapshot build failed: {Error}", resp.ErrorMessage);
            _pendingAdvice.Remove(resp.CorrelationId);
            ReplyToChat(ctxItem.ReplyChatId, "Не удалось собрать данные для совета. Попробуй позже.");
            return;
        }

        // Запоминаем snapshot — пригодится в случае Unavailable (fallback на local).
        _pendingAdvice[resp.CorrelationId] = ctxItem with { SnapshotForLocal = resp.Snapshot };

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<ClaudeConsultantSingletonMarker>(out var claude))
        {
            // Нет Claude — делаем локальный совет сразу.
            FallbackToLocalAdvice(resp.CorrelationId, resp.Snapshot, ctxItem.TickType, ctxItem.ReplyChatId, persistAnswer: ctxItem.TickType == AdvisorTickType.OnDemand);
            return;
        }

        var userPrompt = AdvicePromptBuilder.Build(resp.Snapshot, ctxItem.TickType);
        var requestedEvt = new ConsultationRequested(
            _userId, resp.CorrelationId, userPrompt, ctxItem.TickType, DateTimeOffset.UtcNow);

        Persist(requestedEvt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();

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
            ApplyEvent(persisted);
            MaybeSnapshot();
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
            // /advice → fallback на локальный совет (требует snapshot, который у нас уже есть).
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

        // tick → park & ждём ClaudeBecameAvailable.
        var parked = new AdviceParked(_userId, ctxItem.TickType, DateTimeOffset.UtcNow);
        Persist(parked, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
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
        // Сохраняем context до получения ответа.
        _pendingAdvice[correlationId] = new PendingAdvice(tickType, replyChatId, snap)
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
        Persist(evt, persisted =>
        {
            ApplyEvent(persisted);
            MaybeSnapshot();
            ReplyToChat(ctxItem.ReplyChatId, text);
        });
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

    private void ReplyOrIgnore(string text)
    {
        if (_state.TelegramId is { } chatId)
        {
            Context.System.EventStream.Publish(new OutgoingTelegramReply(chatId, text));
        }
    }

    private void ReplyToChat(long? chatId, string text)
    {
        if (chatId is not { } id) return;
        Context.System.EventStream.Publish(new OutgoingTelegramReply(id, text));
    }

    private sealed record PendingAdvice(
        AdvisorTickType TickType,
        long? ReplyChatId,
        AdvisorSnapshot? SnapshotForLocal)
    {
        public bool ShouldPersistLocalAnswer { get; init; }
    }
}

internal static class AdviceSystemPrompts
{
    public const string AdviceSystem = """
        Ты — финансовый консультант. Дай краткий совет на основе данных пользователя.
        Ограничение: ответ до 1500 символов, одно цельное сообщение, без markdown-таблиц.
        """;
}
