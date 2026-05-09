using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceBot.Domain.Services;
using FinanceBot.Domain.ValueObjects;

namespace FinanceBot.Infrastructure.CategoryRules;

/// <summary>
/// Реализация <see cref="ICategoryRules"/>, загружающая правила из embedded resource <c>rules.json</c>.
/// Правила сохраняются в порядке убывания длины ключевого слова, чтобы при матче выбиралась самая длинная подстрока.
/// </summary>
public sealed class JsonCategoryRules : ICategoryRules
{
    private readonly Rule[] _rules;

    public JsonCategoryRules() : this(LoadFromEmbeddedResource())
    {
    }

    private JsonCategoryRules(IEnumerable<RuleDto> source)
    {
        _rules = source
            .SelectMany(r => r.Keywords.Select(k => new Rule(r.Category, k.ToLowerInvariant().Trim())))
            .Where(r => r.Keyword.Length > 0)
            .OrderByDescending(r => r.Keyword.Length)
            .ToArray();
    }

    public Category? Match(NormalizedDescription description)
    {
        if (description.IsEmpty)
        {
            return null;
        }

        var text = description.Value;
        foreach (var rule in _rules)
        {
            if (text.Contains(rule.Keyword, StringComparison.Ordinal))
            {
                return rule.Category;
            }
        }
        return null;
    }

    private static IEnumerable<RuleDto> LoadFromEmbeddedResource()
    {
        var assembly = typeof(JsonCategoryRules).Assembly;
        const string resource = "FinanceBot.Infrastructure.CategoryRules.rules.json";

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resource}' not found. Available: " +
                string.Join(", ", assembly.GetManifestResourceNames()));

        var doc = JsonSerializer.Deserialize<RulesFileDto>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse embedded resource '{resource}'.");

        return doc.Rules;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Rule(Category Category, string Keyword);

    private sealed record RulesFileDto(int Version, RuleDto[] Rules);

    private sealed record RuleDto(Category Category, string[] Keywords);
}
