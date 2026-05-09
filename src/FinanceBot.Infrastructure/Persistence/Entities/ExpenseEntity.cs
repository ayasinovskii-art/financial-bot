namespace FinanceBot.Infrastructure.Persistence.Entities;

/// <summary>Read-model расхода. Заполняется ExpenseProjection.</summary>
public sealed class ExpenseEntity
{
    public Guid ExpenseId { get; set; }
    public Guid UserId { get; set; }
    public Guid PeriodId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string Bucket { get; set; } = "None";
    public string Source { get; set; } = "manual";
    public bool NeedsReview { get; set; }
    public bool AutoConfirmed { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid? PlannedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
