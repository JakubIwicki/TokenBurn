using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TokenBurn.Processor.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_aliases",
                schema: "telemetry",
                columns: table => new
                {
                    alias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_aliases", x => x.alias);
                });

            migrationBuilder.CreateTable(
                name: "model_prices",
                schema: "telemetry",
                columns: table => new
                {
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    input_per_mtok = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false),
                    cache_read_per_mtok = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false),
                    cache_write_per_mtok = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false),
                    output_per_mtok = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false),
                    context_window = table.Column<int>(type: "integer", nullable: true),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_prices", x => new { x.slug, x.service, x.effective_from });
                });

            migrationBuilder.CreateIndex(
                name: "IX_model_prices_slug_service_effective_from",
                schema: "telemetry",
                table: "model_prices",
                columns: new[] { "slug", "service", "effective_from" },
                descending: new[] { false, false, true });

            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;

                ALTER TABLE telemetry.model_prices
                    ADD CONSTRAINT ex_model_prices_no_overlap EXCLUDE USING gist (
                        slug WITH =, service WITH =,
                        tstzrange(effective_from, effective_to) WITH &&
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE telemetry.model_prices DROP CONSTRAINT IF EXISTS ex_model_prices_no_overlap;
                DROP EXTENSION IF EXISTS btree_gist;
                """);

            migrationBuilder.DropTable(
                name: "model_aliases",
                schema: "telemetry");

            migrationBuilder.DropTable(
                name: "model_prices",
                schema: "telemetry");
        }
    }
}
