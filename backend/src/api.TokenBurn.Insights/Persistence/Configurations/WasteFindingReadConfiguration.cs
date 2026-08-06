using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.TokenBurn.Insights.Persistence.Configurations;

/// <summary>
///     Column mapping for the read-only <c>telemetry.waste_findings</c> projection.
///     Mirrors the Processor's <c>WasteFindingConfiguration</c> exactly; the table
///     lives in the <c>telemetry</c> schema with <c>insights_role</c> holding
///     SELECT. Evidence columns are deliberately absent from the projection —
///     the summary contract carries no content.
/// </summary>
public sealed class WasteFindingReadConfiguration : IEntityTypeConfiguration<WasteFindingReadModel>
{
    public void Configure(EntityTypeBuilder<WasteFindingReadModel> entity)
    {
        entity.ToTable("waste_findings", "telemetry");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.RunId).HasColumnName("run_id");
        entity.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(50).IsRequired();
        entity.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(50).IsRequired();
        entity.Property(x => x.WastedCostUsd).HasColumnName("wasted_cost_usd").HasPrecision(20, 10);
        entity.Property(x => x.DetectedAt).HasColumnName("detected_at");
        entity.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at");
        entity.Property(x => x.Version).HasColumnName("version");
        // The keyset cursor rides the (detected_at DESC, id DESC) index created by the
        // Processor's AddWasteFindingsKeysetIndex migration — matches the ORDER BY exactly.
        entity.HasIndex(x => new { x.DetectedAt, x.Id }).IsDescending([true, true]);
    }
}
