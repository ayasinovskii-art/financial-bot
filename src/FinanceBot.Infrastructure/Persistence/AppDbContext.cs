using FinanceBot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Infrastructure.Persistence;

/// <summary>
/// EF Core контекст для read-model таблиц схемы <c>app</c>.
/// Схема <c>akka</c> управляется Akka.Persistence.PostgreSql и в этом контексте не описывается.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string AppSchema = "app";

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<PeriodEntity> Periods => Set<PeriodEntity>();
    public DbSet<ExpenseEntity> Expenses => Set<ExpenseEntity>();
    public DbSet<IncomeEntity> Incomes => Set<IncomeEntity>();
    public DbSet<WhitelistEntity> Whitelist => Set<WhitelistEntity>();
    public DbSet<ProjectionOffsetEntity> ProjectionOffsets => Set<ProjectionOffsetEntity>();
    public DbSet<SystemHeartbeatEntity> SystemHeartbeat => Set<SystemHeartbeatEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(AppSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
