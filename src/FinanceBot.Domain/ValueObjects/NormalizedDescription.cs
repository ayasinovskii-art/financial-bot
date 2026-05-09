using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Нормализованная форма описания траты: trim + lowercase + collapse whitespace.
/// Используется как ключ для memory-категоризации.
/// </summary>
public readonly partial record struct NormalizedDescription
{
    public string Value { get; }

    private NormalizedDescription(string value) => Value = value;

    public static NormalizedDescription FromRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new NormalizedDescription(string.Empty);
        }

        var lowered = raw.Trim().ToLower(CultureInfo.GetCultureInfo("ru-RU"));
        var collapsed = Whitespace().Replace(lowered, " ");
        return new NormalizedDescription(collapsed);
    }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
