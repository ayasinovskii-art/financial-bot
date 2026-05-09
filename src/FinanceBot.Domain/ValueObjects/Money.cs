namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Денежная сумма. Иммутабельный value object.
/// Currency хранится как ISO-код, по умолчанию RUB.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency = "RUB")
{
    public static readonly string DefaultCurrency = "RUB";

    public static Money Zero { get; } = new(0m, DefaultCurrency);

    public static Money Rub(decimal amount) => new(amount, DefaultCurrency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public bool IsPositive => Amount > 0m;
    public bool IsNegative => Amount < 0m;
    public bool IsZero => Amount == 0m;

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Currency mismatch: {Currency} vs {other.Currency}.");
        }
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
