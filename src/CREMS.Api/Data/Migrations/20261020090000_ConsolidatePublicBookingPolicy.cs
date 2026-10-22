using CREMS.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261020090000_ConsolidatePublicBookingPolicy")]
public partial class ConsolidatePublicBookingPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Quote-only services were visible in the old catalogue. The division policy
        // now decides the route, so keep those services available for online requests.
        migrationBuilder.Sql("UPDATE ServiceOfferings SET IsBookableOnline = 1 WHERE RequiresQuote = 1 AND IsBookableOnline = 0;");

        // Existing explicitly configured divisions are untouched. For legacy divisions,
        // carry their quote-only route and unambiguous personnel default into one policy.
        migrationBuilder.Sql("""
            UPDATE d SET CustomerBookingConfigurationJson =
                JSON_MODIFY(
                    JSON_MODIFY('{}', '$.quoteMode',
                        CASE WHEN EXISTS (SELECT 1 FROM ServiceOfferings s
                            WHERE s.DivisionId = d.Id AND s.IsActive = 1 AND s.RequiresQuote = 1)
                        THEN 'Always' ELSE 'Automatic' END),
                    '$.personnelRequirement',
                    CASE WHEN (SELECT COUNT(DISTINCT s.PersonnelRequirement) FROM ServiceOfferings s
                        WHERE s.DivisionId = d.Id AND s.IsActive = 1) = 1
                    THEN CASE (SELECT MAX(s.PersonnelRequirement) FROM ServiceOfferings s
                        WHERE s.DivisionId = d.Id AND s.IsActive = 1)
                        WHEN 2 THEN 'Required' WHEN 1 THEN 'Optional' ELSE 'None' END
                    ELSE 'None' END)
            FROM Divisions d
            WHERE d.CustomerBookingConfigurationJson IS NULL
               OR LTRIM(RTRIM(d.CustomerBookingConfigurationJson)) IN ('', '{}');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not reverse business settings edited after this data migration.
    }
}
