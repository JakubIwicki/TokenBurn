using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TokenBurn.Processor.Persistence.Configurations;

public sealed class ModelPriceConfiguration : IEntityTypeConfiguration<ModelPrice>
{
    public void Configure(EntityTypeBuilder<ModelPrice> entity)
    {
        entity.ToTable(TableNames.ModelPrices);
        entity.HasKey(x => new { x.Slug, x.Service, x.EffectiveFrom });
        entity.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(200).IsRequired();
        entity.Property(x => x.Service).HasColumnName("service").HasMaxLength(200).IsRequired();
        entity.Property(x => x.InputPerMtok).HasColumnName("input_per_mtok").HasPrecision(20, 10);
        entity.Property(x => x.CacheReadPerMtok).HasColumnName("cache_read_per_mtok").HasPrecision(20, 10);
        entity.Property(x => x.CacheWritePerMtok).HasColumnName("cache_write_per_mtok").HasPrecision(20, 10);
        entity.Property(x => x.OutputPerMtok).HasColumnName("output_per_mtok").HasPrecision(20, 10);
        entity.Property(x => x.ContextWindow).HasColumnName("context_window");
        entity.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        entity.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        // Newest-first lookup: the as-of query orders by effective_from DESC.
        entity.HasIndex(x => new { x.Slug, x.Service, x.EffectiveFrom }).IsDescending([false, false, true]);
    }
}
