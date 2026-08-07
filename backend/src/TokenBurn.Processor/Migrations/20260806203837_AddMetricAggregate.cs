using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TokenBurn.Processor.Migrations
{
    /// <inheritdoc />
    public partial class AddMetricAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "metrics");

            migrationBuilder.CreateTable(
                name: "aggregate",
                schema: "metrics",
                columns: table => new
                {
                    bucket_day = table.Column<DateOnly>(type: "date", nullable: false),
                    model_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    run_count = table.Column<long>(type: "bigint", nullable: false),
                    priced_run_count = table.Column<long>(type: "bigint", nullable: false),
                    message_count = table.Column<long>(type: "bigint", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_read_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_write_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aggregate", x => new { x.bucket_day, x.model_slug, x.service });
                });

            migrationBuilder.CreateIndex(
                name: "IX_aggregate_model_slug_bucket_day",
                schema: "metrics",
                table: "aggregate",
                columns: new[] { "model_slug", "bucket_day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aggregate",
                schema: "metrics");
        }
    }
}
