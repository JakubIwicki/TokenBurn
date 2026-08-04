using Api.TokenBurn.Ingest.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.TokenBurn.Ingest.Persistence.Configurations;

public sealed class EnvelopeConfiguration : IEntityTypeConfiguration<Envelope>
{
    public void Configure(EntityTypeBuilder<Envelope> entity)
    {
        entity.ToTable(TableNames.Envelopes);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.Source).HasColumnName("source").HasMaxLength(200).IsRequired();
        entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        entity.Property(x => x.ReceivedAt).HasColumnName("received_at").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        entity.HasIndex(x => x.ContentHash).IsUnique();
        entity.HasIndex(x => new { x.Status, x.ReceivedAt });
    }
}
