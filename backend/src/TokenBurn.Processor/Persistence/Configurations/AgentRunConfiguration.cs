using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence.Configurations;

public sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> entity)
    {
        entity.ToTable(TableNames.AgentRuns, table =>
        {
            table.HasCheckConstraint("ck_agent_runs_status", "status IN ('Running','Completed','Failed','Cancelled','Unknown')");
            table.HasCheckConstraint("ck_agent_runs_pricing_status", "pricing_status IN ('Priced','Quarantined','Unpriceable')");
            table.HasCheckConstraint("ck_agent_runs_parent_not_self", "parent_run_id <> id");
        });
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
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        entity.Property(x => x.PricingStatus).HasColumnName("pricing_status").HasConversion<string>().IsRequired();
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
        entity.HasIndex(x => new { x.SessionId, x.AgentId }).IsUnique();
        // PLAN.md §5: (started_at DESC, id) — newest-first for the ledger view, id as tiebreaker.
        entity.HasIndex(x => new { x.StartedAt, x.Id }).IsDescending([true, false]);
        entity.HasIndex(x => new { x.ModelSlug, x.StartedAt });
        entity.HasIndex(x => new { x.Persona, x.StartedAt });
    }
}
