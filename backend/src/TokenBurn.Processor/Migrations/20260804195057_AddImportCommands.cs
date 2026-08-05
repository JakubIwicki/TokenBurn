using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TokenBurn.Processor.Migrations
{
    /// <inheritdoc />
    public partial class AddImportCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_commands",
                schema: "telemetry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    handling_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cooldown_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_commands", x => x.id);
                    table.CheckConstraint("ck_import_commands_status", "status IN ('Queued','Running','Completed','Failed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_commands_status_cooldown_until",
                schema: "telemetry",
                table: "import_commands",
                columns: new[] { "status", "cooldown_until" });

            migrationBuilder.CreateIndex(
                name: "IX_import_commands_type_payload",
                schema: "telemetry",
                table: "import_commands",
                columns: new[] { "type", "payload" },
                unique: true,
                filter: "status IN ('Queued','Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_commands",
                schema: "telemetry");
        }
    }
}
