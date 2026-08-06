using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence.Configurations;

public sealed class WasteFindingConfiguration : IEntityTypeConfiguration<WasteFinding>
{
    public void Configure(EntityTypeBuilder<WasteFinding> entity)
    {
        entity.ToTable(TableNames.WasteFindings, table =>
        {
            table.HasCheckConstraint("ck_waste_findings_kind", "kind IN ('ContextReplay','Loop','CostThreshold')");
            table.HasCheckConstraint("ck_waste_findings_severity", "severity IN ('Minor','Major','Critical')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        entity.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().IsRequired();
        entity.Property(x => x.Severity).HasColumnName("severity").HasConversion<string>().IsRequired();
        entity.Property(x => x.Evidence).HasColumnName("evidence").HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64).IsRequired();
        entity.Property(x => x.WastedCostUsd).HasColumnName("wasted_cost_usd").HasPrecision(20, 10);
        entity.Property(x => x.DetectedAt).HasColumnName("detected_at").IsRequired();
        entity.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at");
        entity.Property(x => x.Version).HasColumnName("version").IsRequired();
        // Findings dedupe on (run_id, kind, evidence_hash) so a replay converges to one row.
        entity.HasIndex(x => new { x.RunId, x.Kind, x.EvidenceHash }).IsUnique();
        entity.HasIndex(x => new { x.Kind, x.Severity, x.DetectedAt }).IsDescending([false, false, true]);
        // Keyset for the Insights /api/findings cursor: (detected_at DESC, id DESC)
        // matches the read endpoint's ORDER BY exactly.
        entity.HasIndex(x => new { x.DetectedAt, x.Id }).IsDescending([true, true]);
    }
}
