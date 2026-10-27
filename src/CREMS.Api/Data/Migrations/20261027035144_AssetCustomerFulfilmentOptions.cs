using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssetCustomerFulfilmentOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FulfilmentMode",
                table: "Assets",
                type: "int",
                nullable: false,
                defaultValue: 0);
            migrationBuilder.Sql("UPDATE Assets SET FulfilmentMode = CASE WHEN RequiresDelivery = 1 THEN 2 ELSE 0 END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FulfilmentMode",
                table: "Assets");
        }
    }
}
