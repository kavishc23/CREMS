using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CREMS.Api.Data.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261118000000_AddSharedIncidentReport")]
public sealed class AddSharedIncidentReport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("AssetId", "RentalIncidents", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>("ReplacementAssetId", "RentalIncidents", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<int>("Outcome", "RentalIncidents", type: "int", nullable: true);
        migrationBuilder.AddColumn<string>("InspectionJson", "RentalIncidents", type: "nvarchar(max)", nullable: true);
        migrationBuilder.Sql("""
            UPDATE incident SET AssetId = item.AssetId
            FROM RentalIncidents incident
            INNER JOIN BookingItems item ON item.BookingId = incident.BookingId
            WHERE (SELECT COUNT(*) FROM BookingItems b WHERE b.BookingId = incident.BookingId) = 1;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("AssetId", "RentalIncidents");
        migrationBuilder.DropColumn("ReplacementAssetId", "RentalIncidents");
        migrationBuilder.DropColumn("Outcome", "RentalIncidents");
        migrationBuilder.DropColumn("InspectionJson", "RentalIncidents");
    }
}
