using System.Collections.Concurrent;
using System.Globalization;
using FinanceBot.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceBot.Infrastructure.WorkdayCalendar;

/// <summary>
/// Реализация <see cref="IWorkdayCalendar"/> поверх isdayoff.ru. Кеш в памяти:
/// бесконечный для прошлых дат, TTL 7 дней для будущих. При недоступности API — fallback на простую логику.
/// </summary>
public sealed class IsDayOffWorkdayCalendar : IWorkdayCalendar
{
    private static readonly TimeSpan FutureCacheTtl = TimeSpan.FromDays(7);
    private const int MaxCacheEntries = 4096;

    private readonly HttpClient _http;
    private readonly WorkdayCalendarOptions _options;
    private readonly ILogger<IsDayOffWorkdayCalendar> _log;
    private readonly ConcurrentDictionary<DateOnly, CachedAnswer> _cache = new();

    public IsDayOffWorkdayCalendar(
        HttpClient http,
        IOptions<WorkdayCalendarOptions> options,
        ILogger<IsDayOffWorkdayCalendar> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public async Task<bool> IsWorkdayAsync(DateOnly date, CancellationToken ct)
    {
        if (_cache.TryGetValue(date, out var cached) && !cached.IsExpired(date))
        {
            return cached.IsWorkday;
        }

        var path = $"/{date:yyyy-MM-dd}?cc={_options.CountryCode}";
        try
        {
            using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("isdayoff returned {Status} for {Date}; fallback to static.", response.StatusCode, date);
                return Fallback(date);
            }
            var body = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
            // 0 — рабочий, 1 — выходной/праздник, 2 — сокращённый рабочий, 4 — выходной (covid и пр.).
            var isWorkday = body == "0" || body == "2";

            _cache[date] = new CachedAnswer(isWorkday, DateTimeOffset.UtcNow);
            EvictIfOversized();
            return isWorkday;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "isdayoff threw for {Date}; fallback to static.", date);
            return Fallback(date);
        }
    }

    public async Task<DateOnly> NextWorkdayOnOrAfterAsync(DateOnly date, CancellationToken ct)
    {
        for (var d = date; ; d = d.AddDays(1))
        {
            if (await IsWorkdayAsync(d, ct).ConfigureAwait(false))
            {
                return d;
            }
        }
    }

    public async Task<DateOnly> PreviousWorkdayOnOrBeforeAsync(DateOnly date, CancellationToken ct)
    {
        for (var d = date; ; d = d.AddDays(-1))
        {
            if (await IsWorkdayAsync(d, ct).ConfigureAwait(false))
            {
                return d;
            }
        }
    }

    private static bool Fallback(DateOnly date)
        => date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;

    private void EvictIfOversized()
    {
        if (_cache.Count <= MaxCacheEntries)
        {
            return;
        }
        // Эвикция «дальних» дат от сегодня — оставляем горячее окно ±1 год.
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        foreach (var key in _cache.Keys)
        {
            var diff = Math.Abs(key.DayNumber - today.DayNumber);
            if (diff > 365)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    private sealed record CachedAnswer(bool IsWorkday, DateTimeOffset CachedAt)
    {
        public bool IsExpired(DateOnly date)
        {
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
            // Прошедшие даты — кеш бессрочен.
            if (date < today)
            {
                return false;
            }
            return DateTimeOffset.UtcNow - CachedAt > FutureCacheTtl;
        }
    }
}
