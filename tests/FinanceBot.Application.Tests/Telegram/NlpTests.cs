using FinanceBot.Application.Actors.Telegram;
using Xunit;

namespace FinanceBot.Application.Tests.Telegram;

public sealed class NlpPreGateTests
{
    [Theory]
    [InlineData("пообедал на 700", true)]
    [InlineData("такси 350р", true)]
    [InlineData("120к", true)]
    [InlineData("получил 50000", true)]
    [InlineData("€25 за Spotify", true)]
    [InlineData("когда мой бюджет?", false)]
    [InlineData("привет", false)]
    public void HasAmount_returns_expected(string text, bool expected)
    {
        Assert.Equal(expected, NlpPreGate.HasAmount(text));
    }
}

public sealed class NlpExpenseParserTests
{
    [Fact]
    public void TryParseResponse_expense_happy_path()
    {
        const string json = """{"type":"expense","amount":700.00,"category":"DiningOut","description":"обед","confidence":0.95,"isFinancial":true}""";

        var ok = NlpExpenseParser.TryParseResponse(json, out var result);

        Assert.True(ok);
        Assert.NotNull(result);
        Assert.Equal("expense", result!.Type);
        Assert.Equal(700m, result.Amount);
        Assert.Equal("DiningOut", result.Category);
        Assert.Equal("обед", result.Description);
        Assert.Equal(0.95, result.Confidence);
        Assert.True(result.IsFinancial);
    }

    [Fact]
    public void TryParseResponse_income_happy_path()
    {
        const string json = """{"type":"income","amount":50000.00,"category":"Other","description":"зарплата","confidence":0.98,"isFinancial":true}""";

        var ok = NlpExpenseParser.TryParseResponse(json, out var result);

        Assert.True(ok);
        Assert.NotNull(result);
        Assert.Equal("income", result!.Type);
        Assert.Equal(50000m, result.Amount);
        Assert.True(result.IsFinancial);
    }

    [Fact]
    public void TryParseResponse_malformed_json_returns_false()
    {
        const string json = "не JSON вообще";

        var ok = NlpExpenseParser.TryParseResponse(json, out var result);

        Assert.False(ok);
        Assert.Null(result);
    }

    [Fact]
    public void TryParseResponse_missing_field_returns_false()
    {
        const string json = """{"type":"expense","amount":100}""";

        var ok = NlpExpenseParser.TryParseResponse(json, out var result);

        Assert.False(ok);
        Assert.Null(result);
    }

    [Fact]
    public void BuildClaudeRequest_uses_ExpenseParse_use_case()
    {
        var correlationId = Guid.NewGuid();
        var request = NlpExpenseParser.BuildClaudeRequest("пообедал на 700", correlationId);

        Assert.Equal(FinanceBot.Domain.Events.Claude.ClaudeUseCase.ExpenseParse, request.UseCase);
        Assert.Equal(correlationId, request.CorrelationId);
        Assert.True(request.MaxTokens <= 256);
        Assert.False(string.IsNullOrWhiteSpace(request.SystemPrompt));
    }
}

public sealed class TelegramRepliesNlpTests
{
    [Fact]
    public void NlpClarifyIncome_returns_non_empty_text_with_amount()
    {
        var result = TelegramReplies.NlpClarifyIncome("зарплата", 50000m);
        Assert.False(string.IsNullOrEmpty(result));
        Assert.Contains("50000", result);
    }
}
