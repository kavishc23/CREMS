using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompleteAssetModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "MaintenanceJobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "MaintenanceJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurrentLocation",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrlsJson",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "InspectionTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    ChecklistJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequiresCustomerSignature = table.Column<bool>(type: "bit", nullable: false),
                    RequiresStaffSignature = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionTemplates_AssetCategories_AssetCategoryId",
                        column: x => x.AssetCategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ResponsesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DamageMapJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MeterReading = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FuelPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    CustomerSignatureName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CustomerSignatureDataUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StaffSignatureName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StaffSignatureDataUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedByName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetInspections_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetInspections_InspectionTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "InspectionTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetInspections_AssetId_CompletedAt",
                table: "AssetInspections",
                columns: new[] { "AssetId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetInspections_TemplateId",
                table: "AssetInspections",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionTemplates_AssetCategoryId_Name_Stage",
                table: "InspectionTemplates",
                columns: new[] { "AssetCategoryId", "Name", "Stage" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetInspections");

            migrationBuilder.DropTable(
                name: "InspectionTemplates");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "CurrentLocation",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "PhotoUrlsJson",
                table: "Assets");
        }
    }
}
