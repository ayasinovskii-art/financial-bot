using System.Globalization;
using System.Net.Http.Headers;

namespace FinanceBot.Infrastructure.Claude;

/// <summary>
/// Парсер заголовков anthropic-ratelimit-* из ответов Claude API.
/// Возвращает оставшиеся токены и время их сброса.
/// </summary>
public static class ClaudeRateLimitParser
{
    public const string TokensRemainingHeader = "anthropic-ratelimit-tokens-remaining";
    public const string TokensResetHeader = "anthropic-ratelimit-tokens-reset";

    public static int? ParseRemainingTokens(HttpResponseHeaders headers)
        => TryParseSingleInt(headers, TokensRemainingHeader);

    public static DateTimeOffset? ParseResetAt(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues(TokensResetHeader, out var values))
        {
            return null;
        }

        var raw = values.FirstOrDefault();
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static int? TryParseSingleInt(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
        {
            return null;
        }
        var raw = values.FirstOrDefault();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
