using FinanceBot.Application.Actors.Telegram;
using FinanceBot.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Telegram;

public sealed class TelegramRepliesGoalTests
{
    [Fact]
    public void GoalUsage_contains_all_subcommands()
    {
        var text = TelegramReplies.GoalUsage();

        text.Should().Contain("add");
        text.Should().Contain("list");
        text.Should().Contain("done");
    }

    [Fact]
    public void GoalAdded_contains_goal_id()
    {
        var id = Guid.NewGuid();
        var text = TelegramReplies.GoalAdded(id);

        text.Should().Contain(id.ToString());
    }

    [Fact]
    public void GoalDone_contains_success_indication()
    {
        var text = TelegramReplies.GoalDone();

        text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GoalNotFound_mentions_list_command()
    {
        var text = TelegramReplies.GoalNotFound();

        text.Should().Contain("list");
    }

    [Fact]
    public void GoalList_empty_suggests_add_command()
    {
        var text = TelegramReplies.GoalList(Array.Empty<GoalState>());

        text.Should().Contain("add");
    }

    [Fact]
    public void GoalList_single_active_goal_shows_number_and_description()
    {
        var goals = new[]
        {
            new GoalState(Guid.NewGuid(), "Скальная школа в горах", null, null, IsCompleted: false)
        };

        var text = TelegramReplies.GoalList(goals);

        text.Should().Contain("1.");
        text.Should().Contain("Скальная школа в горах");
    }

    [Fact]
    public void GoalList_completed_goals_not_shown()
    {
        var goals = new[]
        {
            new GoalState(Guid.NewGuid(), "Завершённая цель", null, null, IsCompleted: true),
            new GoalState(Guid.NewGuid(), "Активная цель", null, null, IsCompleted: false)
        };

        var text = TelegramReplies.GoalList(goals);

        text.Should().Contain("Активная цель");
        text.Should().NotContain("Завершённая цель");
    }

    [Fact]
    public void GoalList_shows_target_amount_when_set()
    {
        var goals = new[]
        {
            new GoalState(Guid.NewGuid(), "Поездка в Японию", TargetAmount: 150_000m, null, IsCompleted: false)
        };

        var text = TelegramReplies.GoalList(goals);

        text.Should().Contain("150000.00");
    }

    [Fact]
    public void GoalList_shows_target_date_when_set()
    {
        var goals = new[]
        {
            new GoalState(Guid.NewGuid(), "Летний отпуск", null, TargetDate: new DateOnly(2027, 8, 1), IsCompleted: false)
        };

        var text = TelegramReplies.GoalList(goals);

        text.Should().Contain("2027-08-01");
    }

    [Fact]
    public void GoalList_multiple_active_goals_numbered_consecutively()
    {
        var goals = new[]
        {
            new GoalState(Guid.NewGuid(), "Цель А", null, null, IsCompleted: false),
            new GoalState(Guid.NewGuid(), "Цель Б", null, null, IsCompleted: false),
            new GoalState(Guid.NewGuid(), "Цель В", null, null, IsCompleted: false),
        };

        var text = TelegramReplies.GoalList(goals);

        text.Should().Contain("1.");
        text.Should().Contain("2.");
        text.Should().Contain("3.");
    }
}
