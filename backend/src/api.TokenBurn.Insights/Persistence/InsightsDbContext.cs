using Api.TokenBurn.Insights.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Api.TokenBurn.Insights.Persistence;

/// <summary>
///     Read-only context over the existing <c>telemetry</c> schema. No
///     migrations — Insights never writes; <c>insights_role</c> is granted
///     SELECT in initdb. Deliberately models only the tables Insights serves.
/// </summary>
public sealed class InsightsDbContext(DbContextOptions<InsightsDbContext> options) : DbContext(options)
{
    public DbSet<AgentRunReadModel> AgentRuns => Set<AgentRunReadModel>();
    public DbSet<WasteFindingReadModel> WasteFindings => Set<WasteFindingReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("telemetry");
        modelBuilder.ApplyConfiguration(new AgentRunReadConfiguration());
        modelBuilder.ApplyConfiguration(new WasteFindingReadConfiguration());
    }
}
