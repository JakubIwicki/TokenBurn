using Microsoft.EntityFrameworkCore;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence.Configurations;

namespace TokenBurn.Processor.Persistence;

public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : DbContext(options)
{
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentMessage> AgentMessages => Set<AgentMessage>();
    public DbSet<WasteFinding> WasteFindings => Set<WasteFinding>();
    public DbSet<ModelPrice> ModelPrices => Set<ModelPrice>();
    public DbSet<ModelAlias> ModelAliases => Set<ModelAlias>();
    public DbSet<ImportCommand> ImportCommands => Set<ImportCommand>();
    public DbSet<SearchDocument> SearchDocuments => Set<SearchDocument>();
    public DbSet<SearchDocumentChunk> SearchDocumentChunks => Set<SearchDocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("telemetry");
        modelBuilder.ApplyConfiguration(new AgentRunConfiguration());
        modelBuilder.ApplyConfiguration(new AgentMessageConfiguration());
        modelBuilder.ApplyConfiguration(new WasteFindingConfiguration());
        modelBuilder.ApplyConfiguration(new ModelPriceConfiguration());
        modelBuilder.ApplyConfiguration(new ModelAliasConfiguration());
        modelBuilder.ApplyConfiguration(new ImportCommandConfiguration());
        modelBuilder.ApplyConfiguration(new SearchDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new SearchDocumentChunkConfiguration());
    }
}
