using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinanceBot.Infrastructure.Persistence.Configurations;

internal sealed class WhitelistEntityConfiguration : IEntityTypeConfiguration<WhitelistEntity>
{
    public void Configure(EntityTypeBuilder<WhitelistEntity> builder)
    {
        builder.ToTable("whitelist", AppDbContext.AppSchema);

        builder.HasKey(x => x.TelegramId);
        builder.Property(x => x.TelegramId).HasColumnName("telegram_id").ValueGeneratedNever();
        builder.Property(x => x.AddedBy).HasColumnName("added_by").IsRequired();
        builder.Property(x => x.AddedAt).HasColumnName("added_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");
    }
}
