using Api.TokenBurn.Ingest.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.TokenBurn.Ingest.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.ToTable(TableNames.OutboxMessages);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.Topic).HasColumnName("topic").HasMaxLength(200).IsRequired();
        entity.Property(x => x.Key).HasColumnName("key").HasMaxLength(500).IsRequired();
        entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        entity.Property(x => x.PublishedAt).HasColumnName("published_at");
        entity.Property(x => x.Attempts).HasColumnName("attempts").IsRequired();
        entity.Property(x => x.DeadLetteredAt).HasColumnName("dead_lettered_at");
        entity.HasIndex(x => new { x.OccurredAt, x.Id }).HasFilter("published_at IS NULL");
    }
}
