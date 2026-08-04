using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TokenBurn.Processor.Migrations
{
    /// <inheritdoc />
    public partial class InitialTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "telemetry");

            migrationBuilder.CreateTable(
                name: "agent_runs",
                schema: "telemetry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agent_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    parent_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    persona = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    model_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    pricing_status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: true),
                    cache_read_tokens = table.Column<long>(type: "bigint", nullable: true),
                    cache_write_tokens = table.Column<long>(type: "bigint", nullable: true),
                    output_tokens = table.Column<long>(type: "bigint", nullable: true),
                    cost_usd = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: true),
                    reported_cost_usd = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: true),
                    price_multiplier = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_runs", x => x.id);
                    table.CheckConstraint("ck_agent_runs_parent_not_self", "parent_run_id <> id");
                    table.CheckConstraint("ck_agent_runs_pricing_status", "pricing_status IN ('Priced','Quarantined','Unpriceable')");
                    table.CheckConstraint("ck_agent_runs_status", "status IN ('Running','Completed','Failed','Cancelled','Unknown')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_model_slug_started_at",
                schema: "telemetry",
                table: "agent_runs",
                columns: new[] { "model_slug", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_persona_started_at",
                schema: "telemetry",
                table: "agent_runs",
                columns: new[] { "persona", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_session_id_agent_id",
                schema: "telemetry",
                table: "agent_runs",
                columns: new[] { "session_id", "agent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_runs_started_at_id",
                schema: "telemetry",
                table: "agent_runs",
                columns: new[] { "started_at", "id" },
                descending: new[] { true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_runs",
                schema: "telemetry");
        }
    }
}
