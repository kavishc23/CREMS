using CREMS.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261022080100_ServiceQuoteChoice")]
public partial class ServiceQuoteChoice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<bool>(name: "AllowsQuoteRequests", table: "ServiceOfferings", type: "bit", nullable: false, defaultValue: true);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "AllowsQuoteRequests", table: "ServiceOfferings");
}
