using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations;

public partial class PerformanceIndexes20261026 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_AuditEvents_BranchId", table: "AuditEvents");
        migrationBuilder.DropIndex(name: "IX_Bookings_CustomerId", table: "Bookings");
        migrationBuilder.AlterColumn<string>(name: "Name", table: "Customers", type: "nvarchar(450)", maxLength: 450, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(max)");
        migrationBuilder.CreateIndex(name: "IX_Assets_IsActive_Status_BranchId_DivisionId", table: "Assets", columns: new[] { "IsActive", "Status", "BranchId", "DivisionId" });
        migrationBuilder.CreateIndex(name: "IX_AuditEvents_BranchId_OccurredAt", table: "AuditEvents", columns: new[] { "BranchId", "OccurredAt" });
        migrationBuilder.CreateIndex(name: "IX_Bookings_CustomerId_CreatedAt", table: "Bookings", columns: new[] { "CustomerId", "CreatedAt" });
        migrationBuilder.CreateIndex(name: "IX_Bookings_Status_BranchId_CreatedAt", table: "Bookings", columns: new[] { "Status", "BranchId", "CreatedAt" });
        migrationBuilder.CreateIndex(name: "IX_ChargeDefinitions_IsActive_DivisionId_ServiceOfferingId_Category", table: "ChargeDefinitions", columns: new[] { "IsActive", "DivisionId", "ServiceOfferingId", "Category" });
        migrationBuilder.CreateIndex(name: "IX_Customers_IsActive_IsBlocked_Name", table: "Customers", columns: new[] { "IsActive", "IsBlocked", "Name" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Assets_IsActive_Status_BranchId_DivisionId", table: "Assets");
        migrationBuilder.DropIndex(name: "IX_AuditEvents_BranchId_OccurredAt", table: "AuditEvents");
        migrationBuilder.DropIndex(name: "IX_Bookings_CustomerId_CreatedAt", table: "Bookings");
        migrationBuilder.DropIndex(name: "IX_Bookings_Status_BranchId_CreatedAt", table: "Bookings");
        migrationBuilder.DropIndex(name: "IX_ChargeDefinitions_IsActive_DivisionId_ServiceOfferingId_Category", table: "ChargeDefinitions");
        migrationBuilder.DropIndex(name: "IX_Customers_IsActive_IsBlocked_Name", table: "Customers");
        migrationBuilder.AlterColumn<string>(name: "Name", table: "Customers", type: "nvarchar(max)", nullable: false, oldClrType: typeof(string), oldType: "nvarchar(450)", oldMaxLength: 450);
        migrationBuilder.CreateIndex(name: "IX_AuditEvents_BranchId", table: "AuditEvents", column: "BranchId");
        migrationBuilder.CreateIndex(name: "IX_Bookings_CustomerId", table: "Bookings", column: "CustomerId");
    }
}
