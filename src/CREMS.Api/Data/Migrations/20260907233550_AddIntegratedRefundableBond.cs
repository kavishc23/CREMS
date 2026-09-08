using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CREMS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegratedRefundableBond : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BondAmountHeld",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BondDeductionAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BondDeductionReason",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BondRefundAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BondSettledAt",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BondStatus",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Preserve refundable deposits already recorded by earlier builds and give every
            // existing booking a usable bond state. PaymentType 1 is the legacy Deposit value.
            migrationBuilder.Sql("""
                UPDATE b
                SET b.BondAmountHeld = ISNULL(p.AmountHeld, 0),
                    b.BondStatus = CASE
                        WHEN b.DepositRequired <= 0 THEN 0
                        WHEN ISNULL(p.AmountHeld, 0) >= b.DepositRequired THEN 2
                        ELSE 1
                    END
                FROM Bookings b
                OUTER APPLY (
                    SELECT SUM(rp.Amount) AS AmountHeld
                    FROM RentalPayments rp
                    WHERE rp.BookingId = b.Id AND rp.Type = 1 AND rp.Status = 0
                ) p;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BondAmountHeld",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "BondDeductionAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "BondDeductionReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "BondRefundAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "BondSettledAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "BondStatus",
                table: "Bookings");
        }
    }
}
