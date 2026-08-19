using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DefaultDepositAmount",
                table: "ServiceOfferings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DefaultHireUnit",
                table: "ServiceOfferings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "InspectionRequirementsJson",
                table: "ServiceOfferings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceRulesJson",
                table: "ServiceOfferings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MeterType",
                table: "ServiceOfferings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiredDocumentsJson",
                table: "ServiceOfferings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresDelivery",
                table: "ServiceOfferings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BrandingJson",
                table: "Divisions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CustomerBookingConfigurationJson",
                table: "Divisions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultApprovalWorkflowId",
                table: "Divisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultCurrency",
                table: "Divisions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DefaultRentalTerms",
                table: "Divisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultTaxRate",
                table: "Divisions",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchManagerUserId",
                table: "Branches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCoverage",
                table: "Branches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Branches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Branches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Branches",
                type: "decimal(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Branches",
                type: "decimal(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupInstructions",
                table: "Branches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalAddress",
                table: "Branches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnInstructions",
                table: "Branches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsBookings",
                table: "BranchDivisions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultApprovalWorkflowId",
                table: "BranchDivisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionManagerUserId",
                table: "BranchDivisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasMaintenanceCapability",
                table: "BranchDivisions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LocalContactEmail",
                table: "BranchDivisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalContactPhone",
                table: "BranchDivisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalTermsJson",
                table: "BranchDivisions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "OpenedOn",
                table: "BranchDivisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingOverridesJson",
                table: "BranchDivisions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "AssetCategoryId",
                table: "Assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceOfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultMeterType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PersonnelRequirement = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetCategories_Divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalTable: "Divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetCategories_ServiceOfferings_ServiceOfferingId",
                        column: x => x.ServiceOfferingId,
                        principalTable: "ServiceOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BranchCalendarExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time", nullable: true),
                    ClosesAt = table.Column<TimeOnly>(type: "time", nullable: true),
                    AfterHoursCharge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchCalendarExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BranchDivisionServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceOfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsBookable = table.Column<bool>(type: "bit", nullable: false),
                    BranchDivisionBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchDivisionDivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchDivisionServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchDivisionServices_BranchDivisions_BranchDivisionBranchId_BranchDivisionDivisionId",
                        columns: x => new { x.BranchDivisionBranchId, x.BranchDivisionDivisionId },
                        principalTable: "BranchDivisions",
                        principalColumns: new[] { "BranchId", "DivisionId" });
                });

            migrationBuilder.CreateTable(
                name: "BranchOperatingPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    PickupCutoff = table.Column<TimeOnly>(type: "time", nullable: true),
                    ReturnCutoff = table.Column<TimeOnly>(type: "time", nullable: true),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    AfterHoursCharge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchOperatingPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetAttributeDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataType = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsSearchable = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAttributeDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetAttributeDefinitions_AssetCategories_AssetCategoryId",
                        column: x => x.AssetCategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetAttributeValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAttributeValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetAttributeValues_AssetAttributeDefinitions_AttributeDefinitionId",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AssetAttributeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAttributeValues_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_AssetCategoryId",
                table: "Assets",
                column: "AssetCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttributeDefinitions_AssetCategoryId_Code",
                table: "AssetAttributeDefinitions",
                columns: new[] { "AssetCategoryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttributeValues_AssetId_AttributeDefinitionId",
                table: "AssetAttributeValues",
                columns: new[] { "AssetId", "AttributeDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetAttributeValues_AttributeDefinitionId",
                table: "AssetAttributeValues",
                column: "AttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_DivisionId_Code",
                table: "AssetCategories",
                columns: new[] { "DivisionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_ServiceOfferingId",
                table: "AssetCategories",
                column: "ServiceOfferingId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchCalendarExceptions_BranchId_Date",
                table: "BranchCalendarExceptions",
                columns: new[] { "BranchId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchDivisionServices_BranchDivisionBranchId_BranchDivisionDivisionId",
                table: "BranchDivisionServices",
                columns: new[] { "BranchDivisionBranchId", "BranchDivisionDivisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchDivisionServices_BranchId_DivisionId_ServiceOfferingId",
                table: "BranchDivisionServices",
                columns: new[] { "BranchId", "DivisionId", "ServiceOfferingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchOperatingPeriods_BranchId_DayOfWeek",
                table: "BranchOperatingPeriods",
                columns: new[] { "BranchId", "DayOfWeek" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_AssetCategories_AssetCategoryId",
                table: "Assets",
                column: "AssetCategoryId",
                principalTable: "AssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_AssetCategories_AssetCategoryId",
                table: "Assets");

            migrationBuilder.DropTable(
                name: "AssetAttributeValues");

            migrationBuilder.DropTable(
                name: "BranchCalendarExceptions");

            migrationBuilder.DropTable(
                name: "BranchDivisionServices");

            migrationBuilder.DropTable(
                name: "BranchOperatingPeriods");

            migrationBuilder.DropTable(
                name: "AssetAttributeDefinitions");

            migrationBuilder.DropTable(
                name: "AssetCategories");

            migrationBuilder.DropIndex(
                name: "IX_Assets_AssetCategoryId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "DefaultDepositAmount",
                table: "ServiceOfferings");

            migrationBuilder.DropColumn(
                name: "DefaultHireUnit",
                table: "ServiceOfferings");

            migrationBuilder.DropColumn(
                name: "InspectionRequirementsJson",
                table: "ServiceOfferings");

            migrationBuilder.DropColumn(
                name: "MaintenanceRulesJson",
                table: "ServiceOfferings");

            migrationBuilder.DropColumn(
                name: "MeterType",
                table: "ServiceOfferings");

            migrationBuilder.DropColumn(
                name: "RequiredDocumentsJson",
                table: "ServiceOfferings");

            migrationBuilder.DropColumn(
                name: "RequiresDelivery",
                table: "ServiceOfferings");

            migrationBuilder.DropColumn(
                name: "BrandingJson",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "CustomerBookingConfigurationJson",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "DefaultApprovalWorkflowId",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "DefaultCurrency",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "DefaultRentalTerms",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "DefaultTaxRate",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "BranchManagerUserId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "DeliveryCoverage",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "PickupInstructions",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "PostalAddress",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "ReturnInstructions",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "AcceptsBookings",
                table: "BranchDivisions");

            migrationBuilder.DropColumn(
                name: "DefaultApprovalWorkflowId",
                table: "BranchDivisions");

            migrationBuilder.DropColumn(
                name: "DivisionManagerUserId",
                table: "BranchDivisions");

            migrationBuilder.DropColumn(
                name: "HasMaintenanceCapability",
                table: "BranchDivisions");

            migrationBuilder.DropColumn(
                name: "LocalContactEmail",
                table: "BranchDivisions");

            migrationBuilder.DropColumn(
                name: "LocalContactPhone",
                table: "BranchDivisions");

            migrationBuilder.DropColumn(
                name: "LocalTermsJson",
                table: "BranchDivisions");

            migrationBuilder.DropColumn(
                name: "OpenedOn",
                table: "BranchDivisions");

            migrationBuilder.DropColumn(
                name: "PricingOverridesJson",
                table: "BranchDivisions");

            migrationBuilder.DropColumn(
                name: "AssetCategoryId",
                table: "Assets");
        }
    }
}
