namespace FinanceBot.Infrastructure.Persistence.Entities;

/// <summary>Сохранённый offset для каждой projection (по тегу EventsByTag).</summary>
public sealed class ProjectionOffsetEntity
{
    public string ProjectionName { get; set; } = string.Empty;
    public long OffsetValue { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}
