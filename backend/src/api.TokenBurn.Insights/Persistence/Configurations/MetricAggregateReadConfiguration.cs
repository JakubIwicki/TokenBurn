using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.TokenBurn.Insights.Persistence.Configurations;

/// <summary>
///     Column mapping for the read-only <c>metrics.aggregate</c> projection.
///     Mirrors the Processor's <c>MetricBucketConfiguration</c> exactly; the
///     table lives in the explicit <c>metrics</c> schema — never the default
///     <c>telemetry</c> schema — with <c>insights_role</c> holding SELECT.
/// </summary>
public sealed class MetricAggregateReadConfiguration : IEntityTypeConfiguration<MetricAggregateReadModel>
{
    public void Configure(EntityTypeBuilder<MetricAggregateReadModel> entity)
    {
        entity.ToTable("aggregate", "metrics");
        entity.HasKey(x => new { x.BucketDay, x.ModelSlug, x.Service });
        entity.Property(x => x.BucketDay).HasColumnName("bucket_day").IsRequired();
        entity.Property(x => x.ModelSlug).HasColumnName("model_slug").HasMaxLength(200).IsRequired();
        entity.Property(x => x.Service).HasColumnName("service").HasMaxLength(200).IsRequired();
        entity.Property(x => x.RunCount).HasColumnName("run_count").IsRequired();
        entity.Property(x => x.PricedRunCount).HasColumnName("priced_run_count").IsRequired();
        entity.Property(x => x.MessageCount).HasColumnName("message_count").IsRequired();
        entity.Property(x => x.InputTokens).HasColumnName("input_tokens").IsRequired();
        entity.Property(x => x.CacheReadTokens).HasColumnName("cache_read_tokens").IsRequired();
        entity.Property(x => x.CacheWriteTokens).HasColumnName("cache_write_tokens").IsRequired();
        entity.Property(x => x.OutputTokens).HasColumnName("output_tokens").IsRequired();
        entity.Property(x => x.CostUsd).HasColumnName("cost_usd").HasPrecision(20, 10).IsRequired();
    }
}
