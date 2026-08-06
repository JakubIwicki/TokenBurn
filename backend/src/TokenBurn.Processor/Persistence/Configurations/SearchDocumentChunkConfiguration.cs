using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence.Configurations;

public sealed class SearchDocumentChunkConfiguration : IEntityTypeConfiguration<SearchDocumentChunk>
{
    public void Configure(EntityTypeBuilder<SearchDocumentChunk> entity)
    {
        entity.ToTable(TableNames.SearchDocumentChunks, TableNames.SearchSchema);
        entity.HasKey(x => x.StoredId);
        entity.Property(x => x.StoredId).HasColumnName("id").ValueGeneratedOnAdd();
        entity.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
        entity.Property(x => x.Ordinal).HasColumnName("ordinal").IsRequired();
        entity.Property(x => x.ChunkText).HasColumnName("chunk_text").HasColumnType("text").IsRequired();
        entity.Property(x => x.TokenCount).HasColumnName("token_count").IsRequired();
        entity.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        // Deterministic chunking fixes the ordinal for a given document, so a re-write of the
        // same document's chunks converges on (document_id, ordinal) instead of duplicating.
        entity.HasIndex(x => new { x.DocumentId, x.Ordinal }).IsUnique();
        entity.HasOne<SearchDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
