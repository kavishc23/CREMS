using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetDeliveryConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresDelivery",
                table: "Assets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE asset
                SET RequiresDelivery = 1
                FROM Assets AS asset
                INNER JOIN Divisions AS division ON division.Id = asset.DivisionId
                WHERE division.Code IN ('CARPTRAC', 'SHIPPING');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresDelivery",
                table: "Assets");
        }
    }
}
