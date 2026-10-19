using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurableHirePricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HireIsTaxable",
                table: "BookingItems",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HireQuantity",
                table: "BookingItems",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HireUnit",
                table: "BookingItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "HireUnit",
                table: "Assets",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "UseServiceHireRate",
                table: "Assets",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HireIsTaxable",
                table: "BookingItems");

            migrationBuilder.DropColumn(
                name: "HireQuantity",
                table: "BookingItems");

            migrationBuilder.DropColumn(
                name: "HireUnit",
                table: "BookingItems");

            migrationBuilder.DropColumn(
                name: "HireUnit",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "UseServiceHireRate",
                table: "Assets");
        }
    }
}
