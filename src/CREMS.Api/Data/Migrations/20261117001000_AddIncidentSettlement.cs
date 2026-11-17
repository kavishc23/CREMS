using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CREMS.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261117001000_AddIncidentSettlement")]
public sealed class AddIncidentSettlement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "IncidentSettlements",
            columns: table => new
            {
                BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                PaymentDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IncidentSettlements", x => x.BookingId);
                table.ForeignKey("FK_IncidentSettlements_Bookings_BookingId", x => x.BookingId,
                    "Bookings", "Id", onDelete: ReferentialAction.Cascade);
            });

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("IncidentSettlements");
}
