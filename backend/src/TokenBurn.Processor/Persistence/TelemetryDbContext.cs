using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence.Configurations;

namespace TokenBurn.Processor.Persistence;

public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : DbContext(options)
{
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<ModelPrice> ModelPrices => Set<ModelPrice>();
    public DbSet<ModelAlias> ModelAliases => Set<ModelAlias>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("telemetry");
        modelBuilder.ApplyConfiguration(new AgentRunConfiguration());
        modelBuilder.ApplyConfiguration(new ModelPriceConfiguration());
        modelBuilder.ApplyConfiguration(new ModelAliasConfiguration());
    }
}
