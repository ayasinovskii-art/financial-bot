using FinanceBot.Application.Actors.Advisor;
using FinanceBot.Domain.Events.Advisor;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Actors;

public sealed class AdvicePromptBuilderGoalTests
{
    private static AdvisorSnapshot EmptySnapshot(IReadOnlyList<GoalSnapshot>? goals = null) =>
        new(
            UserId: Guid.NewGuid(),
            BuiltAt: DateTimeOffset.UtcNow,
            CurrentPeriod: null,
            PreviousPeriod: null,
            CurrentByCategory: Array.Empty<CategorySnapshot>(),
            PreviousByCategory: Array.Empty<CategorySnapshot>(),
            TopExpenses: Array.Empty<TopExpense>(),
            DaysToEndOfPeriod: null,
            Settings: new Dictionary<string, string>())
        {
            ActiveGoals = goals ?? Array.Empty<GoalSnapshot>()
        };

    [Fact]
    public void Build_includes_goals_section_when_ActiveGoals_non_empty()
    {
        var snap = EmptySnapshot(new[]
        {
            new GoalSnapshot(Guid.NewGuid(), "Скальная школа в горах", null, null)
        });

        var prompt = AdvicePromptBuilder.Build(snap, AdvisorTickType.OnDemand);

        prompt.Should().Contain("Финансовые цели");
        prompt.Should().Contain("Скальная школа в горах");
    }

    [Fact]
    public void Build_omits_goals_section_when_ActiveGoals_empty()
    {
        var snap = EmptySnapshot(Array.Empty<GoalSnapshot>());

        var prompt = AdvicePromptBuilder.Build(snap, AdvisorTickType.OnDemand);

        prompt.Should().NotContain("Финансовые цели");
    }

    [Fact]
    public void Build_includes_target_amount_when_set()
    {
        var snap = EmptySnapshot(new[]
        {
            new GoalSnapshot(Guid.NewGuid(), "Путешествие в Японию", TargetAmount: 150_000m, null)
        });

        var prompt = AdvicePromptBuilder.Build(snap, AdvisorTickType.OnDemand);

        prompt.Should().Contain("150000.00");
    }

    [Fact]
    public void Build_includes_target_date_when_set()
    {
        var snap = EmptySnapshot(new[]
        {
            new GoalSnapshot(Guid.NewGuid(), "Летний отпуск", null, TargetDate: new DateOnly(2027, 8, 1))
        });

        var prompt = AdvicePromptBuilder.Build(snap, AdvisorTickType.OnDemand);

        prompt.Should().Contain("2027-08-01");
    }

    [Fact]
    public void Build_omits_amount_and_date_parts_when_not_set()
    {
        var snap = EmptySnapshot(new[]
        {
            new GoalSnapshot(Guid.NewGuid(), "Велосипед", null, null)
        });

        var prompt = AdvicePromptBuilder.Build(snap, AdvisorTickType.OnDemand);

        prompt.Should().Contain("Велосипед");
        prompt.Should().NotContain("цель:");
        prompt.Should().NotContain("| к ");
    }

    [Fact]
    public void Build_includes_all_goals_when_multiple_present()
    {
        var snap = EmptySnapshot(new[]
        {
            new GoalSnapshot(Guid.NewGuid(), "Цель Альфа", null, null),
            new GoalSnapshot(Guid.NewGuid(), "Цель Бета", null, null),
        });

        var prompt = AdvicePromptBuilder.Build(snap, AdvisorTickType.Weekly);

        prompt.Should().Contain("Цель Альфа");
        prompt.Should().Contain("Цель Бета");
    }
}
