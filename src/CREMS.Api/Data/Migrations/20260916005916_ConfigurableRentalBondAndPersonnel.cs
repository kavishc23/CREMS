using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurableRentalBondAndPersonnel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InheritBond",
                table: "ServiceOfferings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultBondAmount",
                table: "Divisions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "InheritBond",
                table: "Assets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PersonnelOverride",
                table: "Assets",
                type: "int",
                nullable: true);
            migrationBuilder.Sql("UPDATE Assets SET PersonnelOverride = PersonnelRequirement;");
            // Previously all vehicles implicitly offered drivers despite the seeded None category.
            // Preserve that behaviour; an explicit asset override can now disable it.
            migrationBuilder.Sql("UPDATE AssetCategories SET PersonnelRequirement = 1 WHERE Code = 'RENTAL_VEHICLE' AND PersonnelRequirement = 0;");
            // Existing assigned services are valid legacy branch configurations. Backfill them once.
            migrationBuilder.Sql("""
                INSERT INTO BranchDivisionServices (Id, BranchId, DivisionId, ServiceOfferingId, IsActive, IsBookable, CreatedAt)
                SELECT NEWID(), a.BranchId, a.DivisionId, a.ServiceOfferingId, 1, 1, SYSDATETIMEOFFSET()
                FROM Assets a
                INNER JOIN BranchDivisions bd ON bd.BranchId = a.BranchId AND bd.DivisionId = a.DivisionId AND bd.IsActive = 1
                INNER JOIN ServiceOfferings s ON s.Id = a.ServiceOfferingId AND s.DivisionId = a.DivisionId AND s.IsActive = 1
                WHERE NOT EXISTS (SELECT 1 FROM BranchDivisionServices bs WHERE bs.BranchId = a.BranchId AND bs.DivisionId = a.DivisionId AND bs.ServiceOfferingId = a.ServiceOfferingId)
                GROUP BY a.BranchId, a.DivisionId, a.ServiceOfferingId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InheritBond",
                table: "ServiceOfferings");

            migrationBuilder.DropColumn(
                name: "DefaultBondAmount",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "InheritBond",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "PersonnelOverride",
                table: "Assets");
        }
    }
}
