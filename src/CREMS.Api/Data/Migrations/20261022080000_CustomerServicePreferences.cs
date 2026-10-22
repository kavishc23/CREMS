using CREMS.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261022080000_CustomerServicePreferences")]
public partial class CustomerServicePreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(name: "PreferredServiceIds", table: "Customers", type: "nvarchar(4000)", maxLength: 4000, nullable: false, defaultValue: "[]");
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "PreferredServiceIds", table: "Customers");
}
