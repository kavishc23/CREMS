using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerHirePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HirePreference",
                table: "Customers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NoPreference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HirePreference",
                table: "Customers");
        }
    }
}
