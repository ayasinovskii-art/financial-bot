using System.Text.Json;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Services;

namespace FinanceBot.Application.Actors.Telegram;

public sealed record NlpParseResult(
    string Type,
    decimal Amount,
    string Category,
    string Description,
    double Confidence,
    bool IsFinancial);

internal static class NlpPreGate
{
    public static bool HasAmount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Fast check: currency symbols alone are sufficient with any digit
        if (text.Contains('₽') || text.Contains('$') || text.Contains('€'))
        {
            foreach (var ch in text)
                if (char.IsAsciiDigit(ch)) return true;
        }

        // AmountTextParser can parse it → plausibly has an amount
        return AmountTextParser.TryParseSingle(text) is not null;
    }
}

internal static class NlpExpenseParser
{
    private const string SystemPrompt =
        """
        You are a financial transaction parser for a Russian-language personal finance bot.
        Analyze the user's message and determine if it describes recording a financial transaction.

        Respond with ONLY valid JSON (no markdown, no explanation):
        {"type":"expense","amount":700.00,"category":"DiningOut","description":"обед","confidence":0.95,"isFinancial":true}

        Fields:
        - type: "expense" or "income"
        - amount: positive decimal, or 0 if not a transaction
        - category: one of: Groceries, DiningOut, Transport, Utilities, Subscriptions, Entertainment, Health, Clothing, Personal, Education, Gifts, Travel, Other
        - description: short Russian label for the item
        - confidence: float 0.0–1.0 (confidence this IS a financial transaction AND you correctly parsed amount/type/category)
        - isFinancial: true only if the message is about recording money spent or received; false for questions, greetings, or budget inquiries

        Examples of isFinancial=false: "сколько осталось?", "когда мой бюджет?", "привет"
        """;

    public static ClaudeRequest BuildClaudeRequest(string text, Guid correlationId) =>
        new(
            UseCase: ClaudeUseCase.ExpenseParse,
            SystemPrompt: SystemPrompt,
            UserPrompt: text,
            MaxTokens: 256,
            CorrelationId: correlationId);

    public static bool TryParseResponse(string json, out NlpParseResult? result)
    {
        result = null;
        try
        {
            using var doc = JsonDocument.Parse(json.Trim());
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString() ?? "expense";
            var amount = root.GetProperty("amount").GetDecimal();
            var category = root.GetProperty("category").GetString() ?? "Other";
            var description = root.GetProperty("description").GetString() ?? string.Empty;
            var confidence = root.GetProperty("confidence").GetDouble();
            var isFinancial = root.GetProperty("isFinancial").GetBoolean();

            result = new NlpParseResult(type, amount, category, description, confidence, isFinancial);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
