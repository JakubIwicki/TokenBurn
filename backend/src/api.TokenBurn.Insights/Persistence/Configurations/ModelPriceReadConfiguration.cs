using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.TokenBurn.Insights.Persistence.Configurations;

/// <summary>
///     Column mapping for the read-only <c>telemetry.model_prices</c> projection.
///     Mirrors the Processor's <c>ModelPriceConfiguration</c> exactly; the table
///     lives in the <c>telemetry</c> schema with <c>insights_role</c> holding
///     SELECT. Explicit allow-list columns only — the registry's operational
///     columns (credential env-var names, upstream hostnames, ports) are never
///     projected (privacy-boundary rule 8).
/// </summary>
public sealed class ModelPriceReadConfiguration : IEntityTypeConfiguration<ModelPriceReadModel>
{
    public void Configure(EntityTypeBuilder<ModelPriceReadModel> entity)
    {
        entity.ToTable("model_prices", "telemetry");
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
    }
}
