using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.TokenBurn.Insights.Persistence.Configurations;

/// <summary>
///     Column mapping for the read-only <c>telemetry.agent_runs</c> projection.
///     Mirrors the Processor's <c>AgentRunConfiguration</c> exactly; the table
///     lives in the <c>telemetry</c> schema with <c>insights_role</c> holding
///     SELECT.
/// </summary>
public sealed class AgentRunReadConfiguration : IEntityTypeConfiguration<AgentRunReadModel>
{
    public void Configure(EntityTypeBuilder<AgentRunReadModel> entity)
    {
        entity.ToTable("agent_runs", "telemetry");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.SessionId).HasColumnName("session_id").HasMaxLength(200).IsRequired();
        entity.Property(x => x.AgentId).HasColumnName("agent_id").HasMaxLength(200).HasDefaultValue("").IsRequired();
        entity.Property(x => x.Source).HasColumnName("source").HasMaxLength(100).IsRequired();
        entity.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(200);
        entity.Property(x => x.ParentRunId).HasColumnName("parent_run_id");
        entity.Property(x => x.Workspace).HasColumnName("workspace").HasMaxLength(500);
        entity.Property(x => x.Persona).HasColumnName("persona").HasMaxLength(500);
        entity.Property(x => x.ModelSlug).HasColumnName("model_slug").HasMaxLength(200);
        entity.Property(x => x.Service).HasColumnName("service").HasMaxLength(200);
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
        entity.Property(x => x.PricingStatus).HasColumnName("pricing_status").HasMaxLength(50).IsRequired();
        entity.Property(x => x.StartedAt).HasColumnName("started_at");
        entity.Property(x => x.EndedAt).HasColumnName("ended_at");
        entity.Property(x => x.InputTokens).HasColumnName("input_tokens");
        entity.Property(x => x.CacheReadTokens).HasColumnName("cache_read_tokens");
        entity.Property(x => x.CacheWriteTokens).HasColumnName("cache_write_tokens");
        entity.Property(x => x.OutputTokens).HasColumnName("output_tokens");
        entity.Property(x => x.CostUsd).HasColumnName("cost_usd").HasPrecision(20, 10);
        entity.Property(x => x.ReportedCostUsd).HasColumnName("reported_cost_usd").HasPrecision(20, 10);
        entity.Property(x => x.PriceMultiplier).HasColumnName("price_multiplier").HasPrecision(6, 3);
        entity.Property(x => x.Version).HasColumnName("version");
        // Mirrors the index the Processor owns, so the keyset cursor rides the
        // existing (started_at DESC, id) index.
        entity.HasIndex(x => new { x.StartedAt, x.Id }).IsDescending([true, false]);
    }
}
