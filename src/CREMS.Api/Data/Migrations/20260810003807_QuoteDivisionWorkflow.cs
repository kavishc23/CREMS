using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class QuoteDivisionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DivisionId",
                table: "SalesQuotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_DivisionId",
                table: "SalesQuotes",
                column: "DivisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesQuotes_Divisions_DivisionId",
                table: "SalesQuotes",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesQuotes_Divisions_DivisionId",
                table: "SalesQuotes");

            migrationBuilder.DropIndex(
                name: "IX_SalesQuotes_DivisionId",
                table: "SalesQuotes");

            migrationBuilder.DropColumn(
                name: "DivisionId",
                table: "SalesQuotes");
        }
    }
}
