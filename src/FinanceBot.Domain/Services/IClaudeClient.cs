using FinanceBot.Domain.Events.Claude;

namespace FinanceBot.Domain.Services;

/// <summary>
/// Запрос к Claude. Иммутабельный.
/// </summary>
public sealed record ClaudeRequest(
    ClaudeUseCase UseCase,
    string SystemPrompt,
    string UserPrompt,
    int MaxTokens,
    Guid CorrelationId);

/// <summary>
/// Ответ Claude (или информация о провале).
/// </summary>
public sealed record ClaudeResponse(
    Guid CorrelationId,
    string? Content,
    bool IsSuccess,
    long LatencyMs,
    int? InputTokens,
    int? OutputTokens,
    int? TokensRemaining,
    DateTimeOffset? TokensResetAt,
    ClaudeUnavailabilityReason? FailureReason,
    string? FailureMessage);

/// <summary>
/// Клиент Anthropic Claude. Реализация — ClaudeClient в Infrastructure (поверх HttpClient + Polly).
/// </summary>
public interface IClaudeClient
{
    Task<ClaudeResponse> SendAsync(ClaudeRequest request, CancellationToken ct);
}
