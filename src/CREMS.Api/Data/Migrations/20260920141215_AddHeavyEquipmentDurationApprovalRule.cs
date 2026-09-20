using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHeavyEquipmentDurationApprovalRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinimumHireDurationDays",
                table: "ApprovalWorkflows",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TriggerForHeavyEquipment",
                table: "ApprovalWorkflows",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinimumHireDurationDays",
                table: "ApprovalWorkflows");

            migrationBuilder.DropColumn(
                name: "TriggerForHeavyEquipment",
                table: "ApprovalWorkflows");
        }
    }
}
