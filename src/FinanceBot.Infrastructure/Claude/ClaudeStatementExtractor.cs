using System.Globalization;
using System.Text.Json;
using FinanceBot.Domain.Events.Claude;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FinanceBot.Infrastructure.Claude;

/// <summary>
/// Распознаёт транзакции из скриншота выписки через Claude Vision (<see cref="IClaudeClient"/> + image-блок).
/// Парсинг ответа вынесен в <see cref="TryParse"/> (internal — покрыт unit-тестами).
/// </summary>
public sealed class ClaudeStatementExtractor : IStatementExtractor
{
    private const int MaxTokens = 4096;

    private const string SystemPrompt =
        "You are a precise bank-statement parser. The user sends a screenshot of a bank statement " +
        "(usually Russian banks: Сбер, Тинькофф, Альфа). Extract EVERY transaction line. " +
        "Respond with ONLY a JSON array — no prose, no markdown code fences. Each element: " +
        "{\"date\":\"YYYY-MM-DD\",\"amount\":<positive number>,\"description\":\"<merchant/description>\"," +
        "\"kind\":\"expense\"|\"income\"}. " +
        "amount is ALWAYS positive. kind=\"income\" for credits/deposits/refunds (зачисление, пополнение), " +
        "kind=\"expense\" for debits/purchases/withdrawals (списание, оплата, перевод). " +
        "Use the statement's year; if a line shows no year, assume the current year. " +
        "If you cannot read any transactions, return [].";

    private const string UserPrompt =
        "Распознай все транзакции с этого скриншота банковской выписки и верни их JSON-массивом по заданной схеме.";

    private readonly IClaudeClient _claude;
    private readonly ILogger<ClaudeStatementExtractor> _log;

    public ClaudeStatementExtractor(IClaudeClient claude, ILogger<ClaudeStatementExtractor> log)
    {
        _claude = claude;
        _log = log;
    }

    public async Task<StatementExtractionResult> ExtractAsync(ReadOnlyMemory<byte> image, string mediaType, CancellationToken ct)
    {
        if (image.IsEmpty)
        {
            return StatementExtractionResult.Failure(ClaudeUnavailabilityReason.Other, "Пустое изображение.");
        }

        var request = new ClaudeRequest(
            ClaudeUseCase.StatementExtraction, SystemPrompt, UserPrompt, MaxTokens, Guid.NewGuid())
        {
            Image = new ClaudeImage(NormalizeMediaType(mediaType), Convert.ToBase64String(image.Span))
        };

        var resp = await _claude.SendAsync(request, ct).ConfigureAwait(false);
        if (!resp.IsSuccess || string.IsNullOrWhiteSpace(resp.Content))
        {
            _log.LogWarning("Statement extraction failed: {Reason} {Message}", resp.FailureReason, resp.FailureMessage);
            return StatementExtractionResult.Failure(
                resp.FailureReason ?? ClaudeUnavailabilityReason.Other,
                resp.FailureMessage ?? "Пустой ответ Claude.");
        }

        if (!TryParse(resp.Content, out var transactions))
        {
            _log.LogWarning("Could not parse statement extraction response as JSON.");
            return StatementExtractionResult.Failure(ClaudeUnavailabilityReason.Other, "Не удалось разобрать ответ распознавания.");
        }

        return StatementExtractionResult.Success(transactions);
    }

    /// <summary>
    /// Извлекает JSON-массив транзакций из ответа Claude (терпимо к prose/markdown-обёртке вокруг массива).
    /// Возвращает false только если массив вообще не найден/не парсится; пустой массив — это true + [].
    /// </summary>
    internal static bool TryParse(string content, out IReadOnlyList<ImportedTransaction> transactions)
    {
        transactions = Array.Empty<ImportedTransaction>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return false;
        }

        var json = content.Substring(start, end - start + 1);
        var list = new List<ImportedTransaction>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object && TryReadTransaction(element, out var txn))
                {
                    list.Add(txn);
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        transactions = list;
        return true;
    }

    private static bool TryReadTransaction(JsonElement element, out ImportedTransaction transaction)
    {
        transaction = null!;

        if (!element.TryGetProperty("amount", out var amountElement))
        {
            return false;
        }

        decimal amount;
        if (amountElement.ValueKind == JsonValueKind.Number)
        {
            amount = amountElement.GetDecimal();
        }
        else if (amountElement.ValueKind == JsonValueKind.String
                 && decimal.TryParse(amountElement.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedAmount))
        {
            amount = parsedAmount;
        }
        else
        {
            return false;
        }

        amount = Math.Abs(amount);
        if (amount <= 0m)
        {
            return false;
        }

        var description = element.TryGetProperty("description", out var descriptionElement)
                          && descriptionElement.ValueKind == JsonValueKind.String
            ? (descriptionElement.GetString() ?? string.Empty).Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = "(импорт)";
        }

        var date = element.TryGetProperty("date", out var dateElement)
                   && dateElement.ValueKind == JsonValueKind.String
                   && DateOnly.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.UtcNow);

        var kind = element.TryGetProperty("kind", out var kindElement)
                   && kindElement.ValueKind == JsonValueKind.String
                   && string.Equals(kindElement.GetString(), "income", StringComparison.OrdinalIgnoreCase)
            ? TransactionKind.Income
            : TransactionKind.Expense;

        transaction = new ImportedTransaction(date, amount, description, kind);
        return true;
    }

    private static string NormalizeMediaType(string mediaType) => mediaType switch
    {
        "image/jpeg" or "image/png" or "image/gif" or "image/webp" => mediaType,
        _ => "image/jpeg"
    };
}
