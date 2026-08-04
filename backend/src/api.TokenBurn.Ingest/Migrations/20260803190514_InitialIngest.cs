using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.TokenBurn.Ingest.Migrations
{
    /// <inheritdoc />
    public partial class InitialIngest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ingest");

            migrationBuilder.CreateTable(
                name: "envelopes",
                schema: "ingest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_envelopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "ingest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    dead_lettered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_envelopes_content_hash",
                schema: "ingest",
                table: "envelopes",
                column: "content_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_envelopes_status_received_at",
                schema: "ingest",
                table: "envelopes",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_occurred_at_id",
                schema: "ingest",
                table: "outbox_messages",
                columns: new[] { "occurred_at", "id" },
                filter: "published_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "envelopes",
                schema: "ingest");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "ingest");
        }
    }
}
