using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceTaxSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaxMode",
                table: "MaintenanceJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TaxOverrideReason",
                table: "MaintenanceJobs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "MaintenanceJobs",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TaxableCosts",
                table: "MaintenanceJobs",
                type: "int",
                nullable: false,
                defaultValue: 9);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxMode",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "TaxOverrideReason",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "TaxableCosts",
                table: "MaintenanceJobs");
        }
    }
}
