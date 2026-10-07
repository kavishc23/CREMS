using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MaintenanceEstimatePresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasEstimate",
                table: "MaintenanceJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);
            // An existing positive amount is an estimate. Legacy zeroes are unknown rather than certified free work.
            migrationBuilder.Sql("UPDATE MaintenanceJobs SET HasEstimate = 1 WHERE EstimatedCost > 0;");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasEstimate",
                table: "MaintenanceJobs");
        }
    }
}
