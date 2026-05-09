using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinanceBot.Infrastructure.Persistence.Configurations;

internal sealed class SystemHeartbeatEntityConfiguration : IEntityTypeConfiguration<SystemHeartbeatEntity>
{
    public void Configure(EntityTypeBuilder<SystemHeartbeatEntity> builder)
    {
        builder.ToTable("system_heartbeat", AppDbContext.AppSchema);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValue(1).ValueGeneratedNever();

        builder.Property(x => x.LastSeen).HasColumnName("last_seen").HasColumnType("timestamptz").IsRequired();

        builder.ToTable(t => t.HasCheckConstraint("ck_system_heartbeat_singleton", "id = 1"));
    }
}
