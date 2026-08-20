using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeCustomerHirePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Customers] SET [HirePreference] = 'NoPreference' WHERE [HirePreference] IS NULL OR [HirePreference] = ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
