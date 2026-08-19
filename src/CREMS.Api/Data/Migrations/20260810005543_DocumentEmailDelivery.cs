using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentEmailDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastEmailId",
                table: "SalesQuotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEmailedAt",
                table: "SalesQuotes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastEmailedTo",
                table: "SalesQuotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastEmailId",
                table: "RentalAgreements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEmailedAt",
                table: "RentalAgreements",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastEmailedTo",
                table: "RentalAgreements",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastEmailId",
                table: "SalesQuotes");

            migrationBuilder.DropColumn(
                name: "LastEmailedAt",
                table: "SalesQuotes");

            migrationBuilder.DropColumn(
                name: "LastEmailedTo",
                table: "SalesQuotes");

            migrationBuilder.DropColumn(
                name: "LastEmailId",
                table: "RentalAgreements");

            migrationBuilder.DropColumn(
                name: "LastEmailedAt",
                table: "RentalAgreements");

            migrationBuilder.DropColumn(
                name: "LastEmailedTo",
                table: "RentalAgreements");
        }
    }
}
