using FinanceBot.Application.Actors.Telegram;
using FluentAssertions;
using Xunit;

namespace FinanceBot.Application.Tests.Telegram;

public sealed class TelegramCommandParserTests
{
    [Theory]
    [InlineData("/start", TelegramCommandKind.Start, "")]
    [InlineData("/help", TelegramCommandKind.Help, "")]
    [InlineData("/whoami", TelegramCommandKind.WhoAmI, "")]
    [InlineData("/cancel", TelegramCommandKind.Cancel, "")]
    [InlineData("/adduser 12345", TelegramCommandKind.AddUser, "12345")]
    [InlineData("/removeuser 12345", TelegramCommandKind.RemoveUser, "12345")]
    [InlineData("/listusers", TelegramCommandKind.ListUsers, "")]
    [InlineData("/settings", TelegramCommandKind.Settings, "")]
    [InlineData("/settings timezone Europe/Moscow", TelegramCommandKind.Settings, "timezone Europe/Moscow")]
    [InlineData("/income 50000 зарплата", TelegramCommandKind.Income, "50000 зарплата")]
    [InlineData("/expense 750 обед", TelegramCommandKind.Expense, "750 обед")]
    [InlineData("/expense_day 1500", TelegramCommandKind.ExpenseDay, "1500")]
    [InlineData("/correct", TelegramCommandKind.Correct, "")]
    [InlineData("/stats", TelegramCommandKind.Stats, "")]
    [InlineData("/stats previous", TelegramCommandKind.Stats, "previous")]
    public void TryParse_recognises_known_commands(string input, TelegramCommandKind kind, string args)
    {
        var parsed = TelegramCommandParser.TryParse(input);
        parsed.Should().NotBeNull();
        parsed!.Kind.Should().Be(kind);
        parsed.ArgumentLine.Should().Be(args);
        parsed.OriginalText.Should().Be(input);
    }

    [Fact]
    public void TryParse_strips_at_botname()
    {
        var parsed = TelegramCommandParser.TryParse("/start@FinanceBot");
        parsed!.Kind.Should().Be(TelegramCommandKind.Start);
    }

    [Fact]
    public void TryParse_handles_at_botname_with_args()
    {
        var parsed = TelegramCommandParser.TryParse("/income@FinanceBot 50000");
        parsed!.Kind.Should().Be(TelegramCommandKind.Income);
        parsed.ArgumentLine.Should().Be("50000");
    }

    [Theory]
    [InlineData("обед 750")]
    [InlineData("not a command")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_returns_null_for_non_commands(string? input)
    {
        TelegramCommandParser.TryParse(input).Should().BeNull();
    }

    [Fact]
    public void Unknown_command_kind()
    {
        var parsed = TelegramCommandParser.TryParse("/foobar arg1");
        parsed!.Kind.Should().Be(TelegramCommandKind.Unknown);
    }
}
