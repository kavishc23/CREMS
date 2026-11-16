using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class RentalExtensionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Approval_adds_hire_and_tax_updates_paid_balance_and_cannot_be_applied_twice(bool savedQuantity, bool taxable, bool invoiceExists)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var booking = new Booking { BookingNumber = "EXT-1", BranchId = asset.BranchId, Status = BookingStatus.ConvertedToRental, TaxRate = 12.5m, DepositRequired = 100, BondAmountHeld = 100 };
        var item = new BookingItem { AssetId = asset.Id, Asset = asset, StartAt = start, EndAt = start.AddDays(2), DailyRate = 100, HireQuantity = savedQuantity ? 2 : null, HireIsTaxable = taxable };
        booking.Items.Add(item);
        var paid = taxable ? 225m : 200m;
        booking.Payments.Add(new RentalPayment { Amount = paid, Type = PaymentType.RentalCharge, Status = PaymentStatus.Recorded, ReceiptNumber = "PICKUP-1", RecordedByName = "Cashier" });
        if (invoiceExists) booking.Invoice = new RentalInvoice { BookingId = booking.Id, InvoiceNumber = "INV-1", LineItemsJson = "[]", Subtotal = 200, TaxAmount = 25, Total = 225, AmountPaid = paid, BalanceDue = 0, Status = InvoiceStatus.Paid,
            Lines = [new InvoiceLine { Description = "Original hire", Quantity = 2, UnitPrice = 100, TaxRate = 12.5m }] };
        var customerCase = Extension(booking, start.AddDays(5));
        db.AddRange(booking, customerCase);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var controller = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        Assert.IsType<OkObjectResult>(await controller.DecideCustomerRequest(booking.Id, customerCase.Id, new(true, "Approved"), Token));
        Assert.Equal(start.AddDays(5), item.EndAt); Assert.Equal(5, item.HireQuantity);
        Assert.Equal(100, item.DailyRate); Assert.Equal(taxable ? 562.5m : 500m, RentalFinancials.Total(booking));
        Assert.Equal(taxable ? 337.5m : 300m, RentalFinancials.Total(booking) - RentalFinancials.Paid(booking));
        Assert.Equal(100, booking.BondAmountHeld); Assert.Equal(paid, RentalFinancials.Paid(booking));
        if (invoiceExists) {
            Assert.Equal(562.5m, booking.Invoice!.Total); Assert.Equal(337.5m, booking.Invoice.BalanceDue);
            Assert.Equal(InvoiceStatus.PartiallyPaid, booking.Invoice.Status);
            Assert.Equal(2, booking.Invoice.Lines.Count); Assert.Contains(booking.Invoice.Lines, x => x.Description.StartsWith("Hire extension") && x.Quantity == 3);
        }
        Assert.IsType<BadRequestObjectResult>(await controller.DecideCustomerRequest(booking.Id, customerCase.Id, new(true, "Retry"), Token));
        Assert.Equal(5, item.HireQuantity);
        Assert.Single(await db.AuditEvents.Where(x => x.Action == "Rental extension priced").ToListAsync(Token));
        var second = Extension(booking, start.AddDays(6)); db.Add(second);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        Assert.IsType<OkObjectResult>(await controller.DecideCustomerRequest(booking.Id, second.Id, new(true, "Second extension"), Token));
        Assert.Equal(6, item.HireQuantity);
        Assert.Equal(taxable ? 675m : 600m, RentalFinancials.Total(booking));
    }

    [Theory]
    [InlineData(ChargeUnit.Hour, 2, 5, 2, 5)]
    [InlineData(ChargeUnit.Day, 25, 47, 2, 2)]
    [InlineData(ChargeUnit.Week, 168, 192, 1, 2)]
    [InlineData(ChargeUnit.Fixed, 24, 48, 1, 1)]
    [InlineData(ChargeUnit.Unit, 24, 48, 4, 4)]
    public void Extensions_follow_the_agreed_billing_unit(ChargeUnit unit, int oldHours, int newHours, decimal initial, decimal expected)
    {
        var start = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(12));
        var item = new BookingItem { StartAt = start, EndAt = start.AddHours(oldHours), HireUnit = unit, HireQuantity = initial, DailyRate = 10 };
        var booking = new Booking { BookingNumber = "EXT", Items = [item] };
        RentalExtensions.Apply(booking, start.AddHours(newHours));
        Assert.Equal(expected, item.HireQuantity); Assert.Equal(expected * 10, HirePricing.Amount(item));
    }

    [Fact]
    public void Monthly_extension_uses_calendar_months_and_preserves_agreed_quantity_adjustments()
    {
        var start = new DateTimeOffset(2026, 1, 31, 9, 0, 0, TimeSpan.FromHours(12));
        var item = new BookingItem { StartAt = start, EndAt = start.AddMonths(1), HireUnit = ChargeUnit.Month, HireQuantity = 1, DailyRate = 900 };
        var booking = new Booking { BookingNumber = "EXT", Items = [item] };
        RentalExtensions.Apply(booking, start.AddMonths(2).ToUniversalTime());
        Assert.Equal(2, item.HireQuantity); Assert.Equal(1800, HirePricing.Amount(item));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Declined_or_conflicting_extension_does_not_change_dates_or_charges(bool conflict)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var booking = new Booking { BookingNumber = "EXT-1", BranchId = asset.BranchId, Status = BookingStatus.Confirmed };
        var item = new BookingItem { Asset = asset, AssetId = asset.Id, StartAt = start, EndAt = start.AddDays(2), DailyRate = 100, HireQuantity = 2 };
        booking.Items.Add(item); var customerCase = Extension(booking, start.AddDays(5)); db.AddRange(booking, customerCase);
        if (conflict) db.Add(new Booking { BookingNumber = "OTHER", Status = BookingStatus.Confirmed,
            Items = [new BookingItem { AssetId = asset.Id, StartAt = start.AddDays(3), EndAt = start.AddDays(4) }] });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var controller = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        var result = await controller.DecideCustomerRequest(booking.Id, customerCase.Id, new(conflict, "Reviewed"), Token);
        if (conflict) Assert.IsType<ConflictObjectResult>(result); else Assert.IsType<OkObjectResult>(result);
        Assert.Equal(start.AddDays(2), item.EndAt); Assert.Equal(2, item.HireQuantity);
        Assert.Equal(200, HirePricing.Amount(item));
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Existing_invoice_keeps_legacy_lines_and_credits_when_extension_is_added(bool inclusive, bool credited)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var start = DateTimeOffset.UtcNow.AddDays(10);
        var booking = new Booking { BookingNumber = "EXT-INVOICE", BranchId = asset.BranchId, Status = BookingStatus.ConvertedToRental, TaxRate = 12.5m };
        booking.Items.Add(new BookingItem { Asset = asset, AssetId = asset.Id, StartAt = start, EndAt = start.AddDays(2), HireQuantity = 2, DailyRate = 100 });
        var invoice = new RentalInvoice { InvoiceNumber = "LEGACY", BookingId = booking.Id, Subtotal = inclusive ? 225 : 200, TaxAmount = 25, Total = credited ? 215 : 225,
            AmountPaid = credited ? 215 : 225, BalanceDue = 0, Status = InvoiceStatus.Paid, TaxInclusive = inclusive,
            LineItemsJson = inclusive ? "[{\"description\":\"Original hire\",\"quantity\":2,\"unitPrice\":112.5,\"isTaxable\":true}]" : "[{\"description\":\"Original hire\",\"quantity\":2,\"unitPrice\":100,\"isTaxable\":true}]" };
        booking.Invoice = invoice;
        if (credited) db.CreditNotes.Add(new CreditNote { CreditNoteNumber = "CREDIT", Invoice = invoice, InvoiceId = invoice.Id, Reason = "Agreed credit", Total = 10, Subtotal = 10, Status = CreditNoteStatus.Applied });
        var customerCase = Extension(booking, start.AddDays(3)); db.AddRange(booking, customerCase);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var controller = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        Assert.IsType<OkObjectResult>(await controller.DecideCustomerRequest(booking.Id, customerCase.Id, new(true, "Approved"), Token));
        Assert.Equal(credited ? 327.5m : 337.5m, invoice.Total); Assert.Equal(112.5m, invoice.BalanceDue);
        Assert.Contains(invoice.Lines, line => line.Description == "Original hire" && line.Quantity == 2);
        Assert.Contains(invoice.Lines, line => line.Description.StartsWith("Hire extension") && line.UnitPrice == (inclusive ? 112.5m : 100m));
        Assert.Equal(credited ? 215 : 225, invoice.AmountPaid);
        if (credited) Assert.Equal(10, (await db.CreditNotes.SingleAsync(Token)).Total);
    }

    private static CustomerCase Extension(Booking booking, DateTimeOffset end) => new() { CaseNumber = $"CASE-{Guid.NewGuid():N}",
        CustomerId = booking.CustomerId, BranchId = booking.BranchId, Subject = $"Extension request — {booking.BookingNumber}", Description = $"RequestedEndAt: {end:O}" };
}
