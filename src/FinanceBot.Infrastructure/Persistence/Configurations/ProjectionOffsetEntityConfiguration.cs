using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinanceBot.Infrastructure.Persistence.Configurations;

internal sealed class ProjectionOffsetEntityConfiguration : IEntityTypeConfiguration<ProjectionOffsetEntity>
{
    public void Configure(EntityTypeBuilder<ProjectionOffsetEntity> builder)
    {
        builder.ToTable("projection_offsets", AppDbContext.AppSchema);

        builder.HasKey(x => x.ProjectionName);
        builder.Property(x => x.ProjectionName)
            .HasColumnName("projection_name")
            .HasMaxLength(64)
            .ValueGeneratedNever();

        builder.Property(x => x.OffsetValue).HasColumnName("offset_value").IsRequired();
        builder.Property(x => x.LastUpdated).HasColumnName("last_updated").HasColumnType("timestamptz").IsRequired();
    }
}
