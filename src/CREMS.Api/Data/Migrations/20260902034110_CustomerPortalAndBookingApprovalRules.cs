using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerPortalAndBookingApprovalRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "HirePreference",
                table: "Customers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultForBookings",
                table: "ApprovalWorkflows",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumAmount",
                table: "ApprovalWorkflows",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "ApprovalWorkflows",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TriggerForEquipment",
                table: "ApprovalWorkflows",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TriggerForOvertime",
                table: "ApprovalWorkflows",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TriggerForPersonnel",
                table: "ApprovalWorkflows",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefaultForBookings",
                table: "ApprovalWorkflows");

            migrationBuilder.DropColumn(
                name: "MinimumAmount",
                table: "ApprovalWorkflows");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "ApprovalWorkflows");

            migrationBuilder.DropColumn(
                name: "TriggerForEquipment",
                table: "ApprovalWorkflows");

            migrationBuilder.DropColumn(
                name: "TriggerForOvertime",
                table: "ApprovalWorkflows");

            migrationBuilder.DropColumn(
                name: "TriggerForPersonnel",
                table: "ApprovalWorkflows");

            migrationBuilder.AlterColumn<string>(
                name: "HirePreference",
                table: "Customers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);
        }
    }
}
