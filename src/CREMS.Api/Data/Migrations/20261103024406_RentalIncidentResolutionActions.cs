using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RentalIncidentResolutionActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BondDeductionAmount",
                table: "RentalIncidents",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CustomerResolution",
                table: "RentalIncidents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FineAmount",
                table: "RentalIncidents",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ImmediateReturnRequested",
                table: "RentalIncidents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "RentalIncidents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByName",
                table: "RentalIncidents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "RentalIncidents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StaffResolution",
                table: "RentalIncidents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WarningIssued",
                table: "RentalIncidents",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BondDeductionAmount",
                table: "RentalIncidents");

            migrationBuilder.DropColumn(
                name: "CustomerResolution",
                table: "RentalIncidents");

            migrationBuilder.DropColumn(
                name: "FineAmount",
                table: "RentalIncidents");

            migrationBuilder.DropColumn(
                name: "ImmediateReturnRequested",
                table: "RentalIncidents");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "RentalIncidents");

            migrationBuilder.DropColumn(
                name: "ReviewedByName",
                table: "RentalIncidents");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "RentalIncidents");

            migrationBuilder.DropColumn(
                name: "StaffResolution",
                table: "RentalIncidents");

            migrationBuilder.DropColumn(
                name: "WarningIssued",
                table: "RentalIncidents");
        }
    }
}
