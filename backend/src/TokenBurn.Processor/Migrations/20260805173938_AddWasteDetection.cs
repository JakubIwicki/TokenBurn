using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TokenBurn.Processor.Migrations
{
    /// <inheritdoc />
    public partial class AddWasteDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "waste_findings",
                schema: "telemetry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "text", nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    evidence_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    wasted_cost_usd = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waste_findings", x => x.id);
                    table.CheckConstraint("ck_waste_findings_kind", "kind IN ('ContextReplay','Loop','CostThreshold')");
                    table.CheckConstraint("ck_waste_findings_severity", "severity IN ('Minor','Major','Critical')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_waste_findings_kind_severity_detected_at",
                schema: "telemetry",
                table: "waste_findings",
                columns: new[] { "kind", "severity", "detected_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_waste_findings_run_id_kind_evidence_hash",
                schema: "telemetry",
                table: "waste_findings",
                columns: new[] { "run_id", "kind", "evidence_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waste_findings",
                schema: "telemetry");
        }
    }
}
