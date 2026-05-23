using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using FinanceBot.Application.Actors.Claude;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Categorizer;

/// <summary>
/// Per-node stateless actor для категоризации трат. Сначала локальные правила (<see cref="ICategoryRules"/>),
/// при miss — делегирование в <see cref="ClaudeConsultantActor"/> (Stage 12).
/// На <see cref="ClaudeUnavailableReply"/> возвращает Other + needsReview=true (fallback).
/// </summary>
public sealed class CategorizerActor : ReceiveActor, IWithTimers
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(35);

    private readonly ICategoryRules _rules;
    private readonly ILoggingAdapter _log;
    private readonly Dictionary<Guid, PendingRequest> _pending = new();

    public ITimerScheduler Timers { get; set; } = null!;

    public CategorizerActor(ICategoryRules rules)
    {
        _rules = rules;
        _log = Context.GetLogger();

        Receive<CategorizeRequest>(HandleRequest);
        Receive<ClaudeOkReply>(HandleClaudeOk);
        Receive<ClaudeUnavailableReply>(HandleClaudeUnavailable);
        Receive<ClaudeDeadline>(HandleDeadline);
    }

    private void HandleRequest(CategorizeRequest req)
    {
        var matched = _rules.Match(req.NormalizedDescription);
        if (matched is { } category)
        {
            _log.Debug("Rules hit for '{Desc}' → {Category}.", req.NormalizedDescription.Value, category);
            Sender.Tell(new CategorizeResponse(req.CorrelationId, req.UserId, req.ExpenseId, category, ExpenseSource.Rules, false));
            return;
        }

        var registry = ActorRegistry.For(Context.System);
        if (!registry.TryGet<ClaudeConsultantSingletonMarker>(out var claude))
        {
            _log.Debug("Claude not registered; rules miss → fallback Other for '{Desc}'.", req.NormalizedDescription.Value);
            Sender.Tell(new CategorizeResponse(req.CorrelationId, req.UserId, req.ExpenseId, Category.Other, ExpenseSource.Fallback, true));
            return;
        }

        var corr = Guid.NewGuid();
        _pending[corr] = new PendingRequest(req, Sender);
        Timers.StartSingleTimer(TimerKey(corr), new ClaudeDeadline(corr), AskTimeout);

        var prompt = req.NormalizedDescription.Value;
        var claudeReq = new ClaudeRequest(
            UseCase: ClaudeUseCase.Categorization,
            SystemPrompt: CategorizerPrompts.CategorizationSystem,
            UserPrompt: prompt,
            MaxTokens: 16,
            CorrelationId: corr);
        claude.Tell(new ClaudeAskMessage(claudeReq));
    }

    private void HandleClaudeOk(ClaudeOkReply reply)
    {
        if (!_pending.Remove(reply.CorrelationId, out var p))
        {
            return;
        }
        Timers.Cancel(TimerKey(reply.CorrelationId));

        var raw = (reply.Content ?? string.Empty).Trim().Trim('"', '`', '.', '\'');
        if (CategoryExtensions.TryParse(raw, out var category))
        {
            p.Sender.Tell(new CategorizeResponse(p.Request.CorrelationId, p.Request.UserId, p.Request.ExpenseId,
                category, ExpenseSource.Claude, false));
        }
        else
        {
            _log.Warning("Claude returned non-category response '{Content}', falling back to Other.", raw);
            p.Sender.Tell(new CategorizeResponse(p.Request.CorrelationId, p.Request.UserId, p.Request.ExpenseId,
                Category.Other, ExpenseSource.Fallback, true));
        }
    }

    private void HandleClaudeUnavailable(ClaudeUnavailableReply reply)
    {
        if (!_pending.Remove(reply.CorrelationId, out var p))
        {
            return;
        }
        Timers.Cancel(TimerKey(reply.CorrelationId));
        p.Sender.Tell(new CategorizeResponse(p.Request.CorrelationId, p.Request.UserId, p.Request.ExpenseId,
            Category.Other, ExpenseSource.Fallback, true));
    }

    private void HandleDeadline(ClaudeDeadline d)
    {
        if (!_pending.Remove(d.CorrelationId, out var p))
        {
            return;
        }
        _log.Warning("Claude did not respond within {Timeout} for corr={Corr}; fallback Other.", AskTimeout, d.CorrelationId);
        p.Sender.Tell(new CategorizeResponse(p.Request.CorrelationId, p.Request.UserId, p.Request.ExpenseId,
            Category.Other, ExpenseSource.Fallback, true));
    }

    private static string TimerKey(Guid corr) => $"claude-deadline-{corr:N}";

    public static Props CreateProps(ICategoryRules rules) => Props.Create(() => new CategorizerActor(rules));

    private sealed record PendingRequest(CategorizeRequest Request, IActorRef Sender);

    private sealed record ClaudeDeadline(Guid CorrelationId);
}

internal static class CategorizerPrompts
{
    public const string CategorizationSystem = """
        Ты помощник, который определяет категорию траты. Категории:
        Groceries, DiningOut, Transport, Utilities, Subscriptions, Entertainment,
        Health, Clothing, Personal, Education, Gifts, Travel, Other.
        Отвечай ОДНИМ словом — название категории из списка.
        """;
}

/// <summary>Запрос на категоризацию траты.</summary>
public sealed record CategorizeRequest(
    Guid CorrelationId,
    Guid UserId,
    Guid ExpenseId,
    NormalizedDescription NormalizedDescription);

/// <summary>Ответ: категория и источник определения.</summary>
public sealed record CategorizeResponse(
    Guid CorrelationId,
    Guid UserId,
    Guid ExpenseId,
    Category Category,
    ExpenseSource Source,
    bool NeedsReview);

/// <summary>Marker для регистрации в registry.</summary>
public sealed class CategorizerActorMarker;
