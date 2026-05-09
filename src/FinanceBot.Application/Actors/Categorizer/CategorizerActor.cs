using Akka.Actor;
using Akka.Event;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Application.Actors.Categorizer;

/// <summary>
/// Per-node stateless actor для категоризации трат. Stage 10 — только локальные правила.
/// Stage 12 расширит до делегирования в Claude при miss правил.
/// </summary>
public sealed class CategorizerActor : ReceiveActor
{
    private readonly ICategoryRules _rules;
    private readonly ILoggingAdapter _log;

    public CategorizerActor(ICategoryRules rules)
    {
        _rules = rules;
        _log = Context.GetLogger();

        Receive<CategorizeRequest>(HandleRequest);
    }

    private void HandleRequest(CategorizeRequest req)
    {
        var matched = _rules.Match(req.NormalizedDescription);
        var category = matched ?? Category.Other;
        var source = matched is null ? ExpenseSource.Fallback : ExpenseSource.Rules;
        var needsReview = matched is null;

        _log.Debug("Categorize '{Desc}' → {Category} (source={Source}).",
            req.NormalizedDescription.Value, category, source);

        Sender.Tell(new CategorizeResponse(req.CorrelationId, req.UserId, req.ExpenseId, category, source, needsReview));
    }

    public static Props CreateProps(ICategoryRules rules) => Props.Create(() => new CategorizerActor(rules));
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
