namespace FinanceBot.Infrastructure.Persistence.Entities;

/// <summary>Бюджетный период. Заполняется PeriodProjection.</summary>
public sealed class PeriodEntity
{
    public Guid PeriodId { get; set; }
    public Guid UserId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Status { get; set; } = "active"; // active | closed
    public decimal TotalIncome { get; set; }
    public decimal AllocationEssentials { get; set; }
    public decimal AllocationFun { get; set; }
    public decimal AllocationDeposit { get; set; }
    public decimal? SavingsActual { get; set; }
}
