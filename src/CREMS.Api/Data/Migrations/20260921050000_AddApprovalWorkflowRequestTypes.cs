using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using CREMS.Api.Data;

#nullable disable

namespace CREMS.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260921050000_AddApprovalWorkflowRequestTypes")]
public partial class AddApprovalWorkflowRequestTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "AppliesToBooking", table: "ApprovalWorkflows", type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(name: "AppliesToQuotation", table: "ApprovalWorkflows", type: "bit", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AppliesToBooking", table: "ApprovalWorkflows");
        migrationBuilder.DropColumn(name: "AppliesToQuotation", table: "ApprovalWorkflows");
    }
}
