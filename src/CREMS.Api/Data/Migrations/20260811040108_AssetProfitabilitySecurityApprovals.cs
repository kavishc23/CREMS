using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssetProfitabilitySecurityApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DowntimeHours",
                table: "MaintenanceJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ExternalServiceCost",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "MaintenanceJobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LabourCost",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MeterReading",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherCost",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PartsCost",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxCost",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransportCost",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AcquisitionCost",
                table: "Assets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AcquisitionDate",
                table: "Assets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentBookValue",
                table: "Assets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentMeterReading",
                table: "Assets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EngineNumber",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InsuranceExpiry",
                table: "Assets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InsurancePolicyNumber",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeterUnit",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModelYear",
                table: "Assets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnershipType",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VinOrChassisNumber",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WarrantyExpiry",
                table: "Assets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentStage",
                table: "ApprovalRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalStages",
                table: "ApprovalRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowId",
                table: "ApprovalRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalStageDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageNumber = table.Column<int>(type: "int", nullable: false),
                    StageName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedRole = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalStageDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalStageDecisions_ApprovalRequests_ApprovalRequestId",
                        column: x => x.ApprovalRequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalWorkflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalWorkflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetCostEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Supplier = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedByName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCostEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetCostEntries_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetCostEntries_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalWorkflowStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedRole = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalWorkflowStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalWorkflowStages_ApprovalWorkflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "ApprovalWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_WorkflowId",
                table: "ApprovalRequests",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalStageDecisions_ApprovalRequestId_StageNumber",
                table: "ApprovalStageDecisions",
                columns: new[] { "ApprovalRequestId", "StageNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflows_Type_BranchId_DivisionId_IsActive",
                table: "ApprovalWorkflows",
                columns: new[] { "Type", "BranchId", "DivisionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalWorkflowStages_WorkflowId_Sequence",
                table: "ApprovalWorkflowStages",
                columns: new[] { "WorkflowId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetCostEntries_AssetId_OccurredOn",
                table: "AssetCostEntries",
                columns: new[] { "AssetId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCostEntries_BookingId",
                table: "AssetCostEntries",
                column: "BookingId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalRequests_ApprovalWorkflows_WorkflowId",
                table: "ApprovalRequests",
                column: "WorkflowId",
                principalTable: "ApprovalWorkflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApprovalRequests_ApprovalWorkflows_WorkflowId",
                table: "ApprovalRequests");

            migrationBuilder.DropTable(
                name: "ApprovalStageDecisions");

            migrationBuilder.DropTable(
                name: "ApprovalWorkflowStages");

            migrationBuilder.DropTable(
                name: "AssetCostEntries");

            migrationBuilder.DropTable(
                name: "ApprovalWorkflows");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequests_WorkflowId",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "DowntimeHours",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "ExternalServiceCost",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "LabourCost",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "MeterReading",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "OtherCost",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "PartsCost",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "TaxCost",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "TransportCost",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "AcquisitionCost",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "AcquisitionDate",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "CurrentBookValue",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "CurrentMeterReading",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "EngineNumber",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "InsuranceExpiry",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "InsurancePolicyNumber",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Manufacturer",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "MeterUnit",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ModelYear",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "OwnershipType",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "VinOrChassisNumber",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "WarrantyExpiry",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "CurrentStage",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "TotalStages",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "WorkflowId",
                table: "ApprovalRequests");
        }
    }
}
