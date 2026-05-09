using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace FinanceBot.Infrastructure.Claude;

/// <summary>
/// HTTP-клиент Anthropic Claude. Polly TimeoutStrategy на запрос; rate-limit заголовки парсятся
/// и отдаются вызывающему через <see cref="ClaudeResponse"/>. Без CB и без retry — за это отвечает
/// ClaudeConsultantActor.
/// </summary>
public sealed class ClaudeClient : IClaudeClient
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _httpClient;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeClient> _log;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public ClaudeClient(HttpClient httpClient, IOptions<ClaudeOptions> options, ILogger<ClaudeClient> log)
    {
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _options = options.Value;
        _log = log;

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(TimeSpan.FromSeconds(_options.Resilience.TimeoutPerAttemptSeconds))
            .Build();
    }

    public async Task<ClaudeResponse> SendAsync(ClaudeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return Failure(request, ClaudeUnavailabilityReason.Other, "Claude:ApiKey is empty.", 0);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var http = BuildHttpRequest(request);
            using var response = await _pipeline.ExecuteAsync(
                async token => await _httpClient.SendAsync(http, token).ConfigureAwait(false), ct);

            sw.Stop();

            var remaining = ClaudeRateLimitParser.ParseRemainingTokens(response.Headers);
            var resetAt = ClaudeRateLimitParser.ParseResetAt(response.Headers);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failure(request,
                    ClaudeUnavailabilityReason.RateLimited,
                    "HTTP 429 Too Many Requests.",
                    sw.ElapsedMilliseconds, remaining, resetAt);
            }

            if ((int)response.StatusCode >= 500)
            {
                return Failure(request,
                    ClaudeUnavailabilityReason.TransientError,
                    $"HTTP {(int)response.StatusCode}.",
                    sw.ElapsedMilliseconds, remaining, resetAt);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return Failure(request,
                    ClaudeUnavailabilityReason.Other,
                    $"HTTP {(int)response.StatusCode}: {Truncate(body, 256)}",
                    sw.ElapsedMilliseconds, remaining, resetAt);
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            var content = ExtractText(doc.RootElement);
            var (input, output) = ExtractUsage(doc.RootElement);

            return new ClaudeResponse(
                CorrelationId: request.CorrelationId,
                Content: content,
                IsSuccess: true,
                LatencyMs: sw.ElapsedMilliseconds,
                InputTokens: input,
                OutputTokens: output,
                TokensRemaining: remaining,
                TokensResetAt: resetAt,
                FailureReason: null,
                FailureMessage: null);
        }
        catch (TimeoutRejectedException)
        {
            sw.Stop();
            return Failure(request, ClaudeUnavailabilityReason.Timeout, "Polly timeout.", sw.ElapsedMilliseconds);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return Failure(request, ClaudeUnavailabilityReason.Timeout, "HttpClient timeout.", sw.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return Failure(request, ClaudeUnavailabilityReason.TransientError, ex.Message, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "Claude request {CorrelationId} threw unexpectedly.", request.CorrelationId);
            return Failure(request, ClaudeUnavailabilityReason.Other, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private HttpRequestMessage BuildHttpRequest(ClaudeRequest request)
    {
        var payload = new
        {
            model = _options.Model,
            max_tokens = request.MaxTokens > 0 ? request.MaxTokens : _options.MaxTokensPerRequest,
            system = request.SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = request.UserPrompt }
            }
        };
        var json = JsonSerializer.Serialize(payload);

        var http = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        http.Headers.Add("x-api-key", _options.ApiKey);
        http.Headers.Add("anthropic-version", AnthropicVersion);
        return http;
    }

    private static string? ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var contentArr) || contentArr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var sb = new StringBuilder();
        foreach (var block in contentArr.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                && block.TryGetProperty("text", out var text))
            {
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }
                sb.Append(text.GetString());
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static (int? Input, int? Output) ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return (null, null);
        }
        var input = usage.TryGetProperty("input_tokens", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : (int?)null;
        var output = usage.TryGetProperty("output_tokens", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : (int?)null;
        return (input, output);
    }

    private static ClaudeResponse Failure(
        ClaudeRequest request,
        ClaudeUnavailabilityReason reason,
        string message,
        long latencyMs,
        int? remaining = null,
        DateTimeOffset? resetAt = null)
        => new(
            CorrelationId: request.CorrelationId,
            Content: null,
            IsSuccess: false,
            LatencyMs: latencyMs,
            InputTokens: null,
            OutputTokens: null,
            TokensRemaining: remaining,
            TokensResetAt: resetAt,
            FailureReason: reason,
            FailureMessage: message);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
