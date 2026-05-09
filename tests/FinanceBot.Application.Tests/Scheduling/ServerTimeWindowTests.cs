using FinanceBot.Application.Scheduling;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Scheduling;

/// <summary>
/// Покрывает детектор пересечения локального времени-точки в UTC-окне.
/// Используется SchedulerActor для ClaudeAutoRecoveryTick (20:00 server) и аналогичных system-тиков.
/// </summary>
public sealed class ServerTimeWindowTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Returns_true_when_window_crosses_target_time()
    {
        var from = new DateTimeOffset(2026, 5, 11, 19, 59, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 5, 11, 20, 0, 0, TimeSpan.Zero);
        ServerTimeWindow.ContainsLocalTimeOfDay(from, to, 20, 0, Utc).Should().BeTrue();
    }

    [Fact]
    public void Returns_false_when_window_does_not_include_target()
    {
        var from = new DateTimeOffset(2026, 5, 11, 18, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 5, 11, 19, 30, 0, TimeSpan.Zero);
        ServerTimeWindow.ContainsLocalTimeOfDay(from, to, 20, 0, Utc).Should().BeFalse();
    }

    [Fact]
    public void Returns_false_when_target_equals_from_boundary_excluded()
    {
        // Полуоткрытое окно (from, to] — target ровно на from не попадает.
        var from = new DateTimeOffset(2026, 5, 11, 20, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 5, 11, 20, 1, 0, TimeSpan.Zero);
        ServerTimeWindow.ContainsLocalTimeOfDay(from, to, 20, 0, Utc).Should().BeFalse();
    }

    [Fact]
    public void Returns_true_when_target_equals_to_boundary_included()
    {
        // (..., to] — target == to попадает.
        var from = new DateTimeOffset(2026, 5, 11, 19, 59, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 5, 11, 20, 0, 0, TimeSpan.Zero);
        ServerTimeWindow.ContainsLocalTimeOfDay(from, to, 20, 0, Utc).Should().BeTrue();
    }

    [Fact]
    public void Returns_false_for_inverted_window()
    {
        var from = new DateTimeOffset(2026, 5, 11, 21, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 5, 11, 18, 0, 0, TimeSpan.Zero);
        ServerTimeWindow.ContainsLocalTimeOfDay(from, to, 20, 0, Utc).Should().BeFalse();
    }

    [Fact]
    public void Detects_target_in_user_timezone_window_spanning_midnight_UTC()
    {
        // TZ Europe/Moscow = UTC+3. 20:00 MSK == 17:00 UTC.
        // Окно 16:30..17:30 UTC → должно поймать 20:00 MSK (== 17:00 UTC).
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var from = new DateTimeOffset(2026, 5, 11, 16, 30, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 5, 11, 17, 30, 0, TimeSpan.Zero);
        ServerTimeWindow.ContainsLocalTimeOfDay(from, to, 20, 0, moscow).Should().BeTrue();
    }
}
