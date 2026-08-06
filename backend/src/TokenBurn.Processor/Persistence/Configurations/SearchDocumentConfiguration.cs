using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence.Configurations;

public sealed class SearchDocumentConfiguration : IEntityTypeConfiguration<SearchDocument>
{
    public void Configure(EntityTypeBuilder<SearchDocument> entity)
    {
        entity.ToTable(TableNames.SearchDocuments, TableNames.SearchSchema);
        entity.HasKey(x => x.StoredId);
        // The id is a server-assigned identity (RETURNING id in the raw-SQL upserter), not a
        // client value: content_hash is the natural key that makes re-imports converge.
        entity.Property(x => x.StoredId).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(x => x.Uri).HasColumnName("uri").HasMaxLength(2048).IsRequired();
        entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        entity.Property(x => x.Source).HasColumnName("source").HasMaxLength(100).IsRequired();
        entity.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        entity.Property(x => x.IndexedAt).HasColumnName("indexed_at").IsRequired();
        // Documents dedupe on content_hash so a re-import of identical content lands on one row.
        entity.HasIndex(x => x.ContentHash).IsUnique();
    }
}
