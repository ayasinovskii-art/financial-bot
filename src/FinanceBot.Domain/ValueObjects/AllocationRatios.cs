namespace FinanceBot.Domain.ValueObjects;

/// <summary>
/// Аллокация бюджета по правилу 50/25/25 (или иной, заданной пользователем).
/// Проценты целые, сумма должна быть равна 100.
/// </summary>
public readonly record struct AllocationRatios
{
    public int EssentialsPercent { get; }
    public int FunPercent { get; }
    public int DepositPercent { get; }

    public AllocationRatios(int essentialsPercent, int funPercent, int depositPercent)
    {
        if (essentialsPercent is < 0 or > 100 ||
            funPercent is < 0 or > 100 ||
            depositPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(essentialsPercent),
                "Each percent must be in 0..100.");
        }
        if (essentialsPercent + funPercent + depositPercent != 100)
        {
            throw new ArgumentException(
                $"Allocation must sum to 100. Got {essentialsPercent}+{funPercent}+{depositPercent}.",
                nameof(essentialsPercent));
        }

        EssentialsPercent = essentialsPercent;
        FunPercent = funPercent;
        DepositPercent = depositPercent;
    }

    public static AllocationRatios Default { get; } = new(50, 25, 25);

    public (decimal Essentials, decimal Fun, decimal Deposit) ApplyTo(decimal totalIncome)
    {
        var essentials = decimal.Round(totalIncome * EssentialsPercent / 100m, 2);
        var fun = decimal.Round(totalIncome * FunPercent / 100m, 2);
        var deposit = totalIncome - essentials - fun;
        return (essentials, fun, deposit);
    }

    public override string ToString()
        => $"{EssentialsPercent}/{FunPercent}/{DepositPercent}";
}
