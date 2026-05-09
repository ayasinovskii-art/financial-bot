using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinanceBot.Infrastructure.Persistence.Configurations;

internal sealed class PeriodEntityConfiguration : IEntityTypeConfiguration<PeriodEntity>
{
    public void Configure(EntityTypeBuilder<PeriodEntity> builder)
    {
        builder.ToTable("periods", AppDbContext.AppSchema);

        builder.HasKey(x => x.PeriodId);
        builder.Property(x => x.PeriodId).HasColumnName("period_id");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.StartDate).HasColumnName("start_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(x => x.TotalIncome).HasColumnName("total_income").HasPrecision(14, 2).IsRequired();
        builder.Property(x => x.AllocationEssentials).HasColumnName("allocation_essentials").HasPrecision(14, 2).IsRequired();
        builder.Property(x => x.AllocationFun).HasColumnName("allocation_fun").HasPrecision(14, 2).IsRequired();
        builder.Property(x => x.AllocationDeposit).HasColumnName("allocation_deposit").HasPrecision(14, 2).IsRequired();
        builder.Property(x => x.SavingsActual).HasColumnName("savings_actual").HasPrecision(14, 2);

        builder.HasIndex(x => new { x.UserId, x.Status });

        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
