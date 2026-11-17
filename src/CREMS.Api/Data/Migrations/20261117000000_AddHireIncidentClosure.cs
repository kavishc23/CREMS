using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CREMS.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261117000000_AddHireIncidentClosure")]
public sealed class AddHireIncidentClosure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "IncidentClosedAt",
            table: "Bookings",
            type: "datetimeoffset",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "IncidentClosedAt", table: "Bookings");
}
