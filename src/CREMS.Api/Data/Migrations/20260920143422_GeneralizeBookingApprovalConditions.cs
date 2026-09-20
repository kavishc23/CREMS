using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeBookingApprovalConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssetTypeCondition",
                table: "ApprovalWorkflows",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConditionMatchMode",
                table: "ApprovalWorkflows",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "HireDurationDays",
                table: "ApprovalWorkflows",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HireDurationOperator",
                table: "ApprovalWorkflows",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE ApprovalWorkflows
                SET AssetTypeCondition = 4,
                    HireDurationOperator = 0,
                    HireDurationDays = CAST(MinimumHireDurationDays AS decimal(18,2)),
                    ConditionMatchMode = 1
                WHERE TriggerForHeavyEquipment = 1 AND MinimumHireDurationDays IS NOT NULL;
                """);

            migrationBuilder.DropColumn(name: "TriggerForHeavyEquipment", table: "ApprovalWorkflows");
            migrationBuilder.DropColumn(name: "MinimumHireDurationDays", table: "ApprovalWorkflows");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "MinimumHireDurationDays", table: "ApprovalWorkflows", type: "int", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "TriggerForHeavyEquipment", table: "ApprovalWorkflows", type: "bit", nullable: false, defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE ApprovalWorkflows
                SET TriggerForHeavyEquipment = 1,
                    MinimumHireDurationDays = CAST(HireDurationDays AS int)
                WHERE AssetTypeCondition = 4 AND HireDurationOperator = 0 AND ConditionMatchMode = 1 AND HireDurationDays IS NOT NULL;
                """);

            migrationBuilder.DropColumn(name: "AssetTypeCondition", table: "ApprovalWorkflows");

            migrationBuilder.DropColumn(
                name: "ConditionMatchMode",
                table: "ApprovalWorkflows");

            migrationBuilder.DropColumn(
                name: "HireDurationDays",
                table: "ApprovalWorkflows");
            migrationBuilder.DropColumn(name: "HireDurationOperator", table: "ApprovalWorkflows");
        }
    }
}
