using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidedOperationsConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssetCategoryId",
                table: "PricingRules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssetId",
                table: "PricingRules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceOfferingId",
                table: "PricingRules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceJson",
                table: "AssetLifecycleEvents",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsCustomerVisible",
                table: "AssetAttributeDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReportable",
                table: "AssetAttributeDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssetCategoryId",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "AssetId",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "ServiceOfferingId",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "EvidenceJson",
                table: "AssetLifecycleEvents");

            migrationBuilder.DropColumn(
                name: "IsCustomerVisible",
                table: "AssetAttributeDefinitions");

            migrationBuilder.DropColumn(
                name: "IsReportable",
                table: "AssetAttributeDefinitions");
        }
    }
}
