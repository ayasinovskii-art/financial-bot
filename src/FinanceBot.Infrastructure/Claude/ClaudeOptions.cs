namespace FinanceBot.Infrastructure.Claude;

/// <summary>Опции Claude (Claude-секция appsettings).</summary>
public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";

    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "claude-sonnet-4-6";
    public int MaxTokensPerRequest { get; init; } = 1024;
    public ResilienceSection Resilience { get; init; } = new();

    public sealed class ResilienceSection
    {
        public int TimeoutPerAttemptSeconds { get; init; } = 30;
        public int ConcurrencyLimit { get; init; } = 3;
        public int TransientUnavailableUntilHour { get; init; } = 20;
    }
}
