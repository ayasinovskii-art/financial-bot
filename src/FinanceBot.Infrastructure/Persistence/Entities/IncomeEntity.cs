namespace FinanceBot.Infrastructure.Persistence.Entities;

/// <summary>Read-model дохода. Заполняется IncomeProjection.</summary>
public sealed class IncomeEntity
{
    public Guid IncomeId { get; set; }
    public Guid UserId { get; set; }
    public Guid PeriodId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
