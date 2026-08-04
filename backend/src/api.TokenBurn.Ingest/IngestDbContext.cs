using Api.TokenBurn.Ingest.Domain;
using Api.TokenBurn.Ingest.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Api.TokenBurn.Ingest;

public sealed class IngestDbContext(DbContextOptions<IngestDbContext> options) : DbContext(options)
{
    public DbSet<Envelope> Envelopes => Set<Envelope>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("ingest");
        builder.ApplyConfiguration(new EnvelopeConfiguration());
        builder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
