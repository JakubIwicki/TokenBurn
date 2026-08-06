using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TokenBurn.Processor.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_messages",
                schema: "telemetry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    tool_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    model_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_read_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_write_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_messages_agent_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "telemetry",
                        principalTable: "agent_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_messages_run_id_occurred_at",
                schema: "telemetry",
                table: "agent_messages",
                columns: new[] { "run_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_messages_run_id_sequence",
                schema: "telemetry",
                table: "agent_messages",
                columns: new[] { "run_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_messages",
                schema: "telemetry");
        }
    }
}
