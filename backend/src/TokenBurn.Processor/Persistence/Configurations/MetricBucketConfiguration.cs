using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence.Configurations;

public sealed class MetricBucketConfiguration : IEntityTypeConfiguration<MetricBucket>
{
    public void Configure(EntityTypeBuilder<MetricBucket> entity)
    {
        // Explicit metrics schema — NEVER the default telemetry schema: this table is the
        // public-safe aggregation surface, kept apart from the private ingestion corpus.
        entity.ToTable(TableNames.MetricAggregates, TableNames.MetricsSchema);
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
        // (model_slug, bucket_day) is the per-day read path the aggregation query fans out on.
        entity.HasIndex(x => new { x.ModelSlug, x.BucketDay });
    }
}
