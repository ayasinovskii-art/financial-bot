using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinanceBot.Infrastructure.Persistence.Configurations;

internal sealed class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.ToTable("users", AppDbContext.AppSchema);

        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).HasColumnName("user_id");

        builder.Property(x => x.TelegramId).HasColumnName("telegram_id").IsRequired();
        builder.HasIndex(x => x.TelegramId).IsUnique();

        builder.Property(x => x.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.SettingsJson)
            .HasColumnName("settings_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.RegisteredAt)
            .HasColumnName("registered_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(x => x.LastUpdated)
            .HasColumnName("last_updated")
            .HasColumnType("timestamptz")
            .IsRequired();
    }
}
