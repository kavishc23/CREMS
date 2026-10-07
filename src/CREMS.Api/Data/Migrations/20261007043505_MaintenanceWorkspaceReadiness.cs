using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MaintenanceWorkspaceReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaintenanceJobs_AssetId",
                table: "MaintenanceJobs");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceJobs_BranchId",
                table: "MaintenanceJobs");

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedPersonnelId",
                table: "MaintenanceJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpectedReleaseAt",
                table: "MaintenanceJobs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FuelCost",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LabourHours",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LabourRate",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseInspectionId",
                table: "MaintenanceJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReleasedAt",
                table: "MaintenanceJobs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleasedByName",
                table: "MaintenanceJobs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedByName",
                table: "MaintenanceJobs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceIntervalMonths",
                table: "MaintenanceJobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceInspectionId",
                table: "MaintenanceJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "MaintenanceJobs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "MaintenanceJobs",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "MaintenanceJobs",
                type: "datetimeoffset",
                nullable: true);

            // Preserve the old release behavior only for assets already operational at migration time.
            // These rows are legacy releases, not newly certified safety inspections.
            migrationBuilder.Sql("""
                UPDATE j SET ReleasedAt = COALESCE(j.CompletedAt, j.UpdatedAt, j.CreatedAt), ReleasedByName = N'Legacy release'
                FROM MaintenanceJobs j JOIN Assets a ON a.Id = j.AssetId
                WHERE j.Status IN (3,4) AND a.Status IN (0,1,2);
                UPDATE MaintenanceJobs SET SourceType = N'Preventive', ReportedByName = N'System'
                WHERE JobNumber LIKE N'MNT-AUTO-%';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceJobs_AssetId_Status_ReleasedAt",
                table: "MaintenanceJobs",
                columns: new[] { "AssetId", "Status", "ReleasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceJobs_BranchId_ReportedAt",
                table: "MaintenanceJobs",
                columns: new[] { "BranchId", "ReportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceJobs_Status_Priority",
                table: "MaintenanceJobs",
                columns: new[] { "Status", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaintenanceJobs_AssetId_Status_ReleasedAt",
                table: "MaintenanceJobs");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceJobs_BranchId_ReportedAt",
                table: "MaintenanceJobs");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceJobs_Status_Priority",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "AssignedPersonnelId",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "ExpectedReleaseAt",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "FuelCost",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "LabourHours",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "LabourRate",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "ReleaseInspectionId",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "ReleasedAt",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "ReleasedByName",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "ReportedByName",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "ServiceIntervalMonths",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "SourceInspectionId",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "MaintenanceJobs");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceJobs_AssetId",
                table: "MaintenanceJobs",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceJobs_BranchId",
                table: "MaintenanceJobs",
                column: "BranchId");
        }
    }
}
