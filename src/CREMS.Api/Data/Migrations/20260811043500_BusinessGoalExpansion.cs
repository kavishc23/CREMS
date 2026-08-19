using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BusinessGoalExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChargeDefinitionId",
                table: "PricingRules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerType",
                table: "PricingRules",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "PricingRules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HolidayMultiplier",
                table: "PricingRules",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeMultiplier",
                table: "PricingRules",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WeekendMultiplier",
                table: "PricingRules",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsPreventive",
                table: "MaintenanceJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "NextServiceMeter",
                table: "MaintenanceJobs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentFailureJobId",
                table: "MaintenanceJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "MaintenanceJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarrantyClaimNumber",
                table: "MaintenanceJobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WarrantyCovered",
                table: "MaintenanceJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "DispatchJobs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryZoneId",
                table: "DispatchJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DistanceKilometres",
                table: "DispatchJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FailedDeliveryCharge",
                table: "DispatchJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "InternalTransportCost",
                table: "DispatchJobs",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "EscalateAfterHours",
                table: "ApprovalWorkflowStages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EscalationRole",
                table: "ApprovalWorkflowStages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalDelegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDelegations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetLifecycleEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MeterReading = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedByName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetLifecycleEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetLifecycleEvents_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetMeterReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reading = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FuelPercent = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetMeterReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetMeterReadings_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessAlertRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Threshold = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LeadTimeHours = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessAlertRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BaseCharge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CostPerKilometre = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ChargePerKilometre = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FailedDeliveryCharge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryZones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenancePartUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaintenanceJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryPartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePartUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenancePartUsages_InventoryParts_InventoryPartId",
                        column: x => x.InventoryPartId,
                        principalTable: "InventoryParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenancePartUsages_MaintenanceJobs_MaintenanceJobId",
                        column: x => x.MaintenanceJobId,
                        principalTable: "MaintenanceJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Personnel",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Availability = table.Column<int>(type: "int", nullable: false),
                    StandardCostRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OvertimeCostRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StandardChargeRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OvertimeChargeRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Personnel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesQuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangeReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteRevisions_SalesQuotes_SalesQuoteId",
                        column: x => x.SalesQuoteId,
                        principalTable: "SalesQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TaxNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DivisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    RaisedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessAlerts_BusinessAlertRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "BusinessAlertRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BookingPersonnelAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CustomerHourlyRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InternalHourlyCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPersonnelAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingPersonnelAssignments_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingPersonnelAssignments_Personnel_PersonnelId",
                        column: x => x.PersonnelId,
                        principalTable: "Personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonnelQualifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CertificateNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    SafetyInduction = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonnelQualifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonnelQualifications_Personnel_PersonnelId",
                        column: x => x.PersonnelId,
                        principalTable: "Personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonnelTimesheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RegularHours = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OvertimeHours = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonnelTimesheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonnelTimesheets_BookingPersonnelAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "BookingPersonnelAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDelegations_FromUserId_StartsAt_EndsAt",
                table: "ApprovalDelegations",
                columns: new[] { "FromUserId", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetLifecycleEvents_AssetId_OccurredAt",
                table: "AssetLifecycleEvents",
                columns: new[] { "AssetId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetMeterReadings_AssetId_Type_RecordedAt",
                table: "AssetMeterReadings",
                columns: new[] { "AssetId", "Type", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingPersonnelAssignments_BookingId",
                table: "BookingPersonnelAssignments",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPersonnelAssignments_PersonnelId_StartAt_EndAt",
                table: "BookingPersonnelAssignments",
                columns: new[] { "PersonnelId", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessAlertRules_Category_BranchId_DivisionId_IsActive",
                table: "BusinessAlertRules",
                columns: new[] { "Category", "BranchId", "DivisionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessAlerts_AcknowledgedAt_RaisedAt",
                table: "BusinessAlerts",
                columns: new[] { "AcknowledgedAt", "RaisedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessAlerts_RuleId",
                table: "BusinessAlerts",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePartUsages_InventoryPartId",
                table: "MaintenancePartUsages",
                column: "InventoryPartId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePartUsages_MaintenanceJobId_InventoryPartId",
                table: "MaintenancePartUsages",
                columns: new[] { "MaintenanceJobId", "InventoryPartId" });

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_EmployeeNumber",
                table: "Personnel",
                column: "EmployeeNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelQualifications_PersonnelId_CertificateNumber",
                table: "PersonnelQualifications",
                columns: new[] { "PersonnelId", "CertificateNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelTimesheets_AssignmentId_WorkDate",
                table: "PersonnelTimesheets",
                columns: new[] { "AssignmentId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevisions_SalesQuoteId_Version",
                table: "QuoteRevisions",
                columns: new[] { "SalesQuoteId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_SupplierNumber",
                table: "Suppliers",
                column: "SupplierNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalDelegations");

            migrationBuilder.DropTable(
                name: "AssetLifecycleEvents");

            migrationBuilder.DropTable(
                name: "AssetMeterReadings");

            migrationBuilder.DropTable(
                name: "BusinessAlerts");

            migrationBuilder.DropTable(
                name: "DeliveryZones");

            migrationBuilder.DropTable(
                name: "MaintenancePartUsages");

            migrationBuilder.DropTable(
                name: "PersonnelQualifications");

            migrationBuilder.DropTable(
                name: "PersonnelTimesheets");

            migrationBuilder.DropTable(
                name: "QuoteRevisions");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "BusinessAlertRules");

            migrationBuilder.DropTable(
                name: "BookingPersonnelAssignments");

            migrationBuilder.DropTable(
                name: "Personnel");

            migrationBuilder.DropColumn(
                name: "ChargeDefinitionId",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "CustomerType",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "HolidayMultiplier",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "OvertimeMultiplier",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "WeekendMultiplier",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "IsPreventive",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "NextServiceMeter",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "ParentFailureJobId",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "WarrantyClaimNumber",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "WarrantyCovered",
                table: "MaintenanceJobs");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "DispatchJobs");

            migrationBuilder.DropColumn(
                name: "DeliveryZoneId",
                table: "DispatchJobs");

            migrationBuilder.DropColumn(
                name: "DistanceKilometres",
                table: "DispatchJobs");

            migrationBuilder.DropColumn(
                name: "FailedDeliveryCharge",
                table: "DispatchJobs");

            migrationBuilder.DropColumn(
                name: "InternalTransportCost",
                table: "DispatchJobs");

            migrationBuilder.DropColumn(
                name: "EscalateAfterHours",
                table: "ApprovalWorkflowStages");

            migrationBuilder.DropColumn(
                name: "EscalationRole",
                table: "ApprovalWorkflowStages");
        }
    }
}
