using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TokenBurn.Processor.Migrations
{
    /// <inheritdoc />
    public partial class AddWasteFindingsKeysetIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_waste_findings_detected_at_id",
                schema: "telemetry",
                table: "waste_findings",
                columns: new[] { "detected_at", "id" },
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_waste_findings_detected_at_id",
                schema: "telemetry",
                table: "waste_findings");
        }
    }
}
