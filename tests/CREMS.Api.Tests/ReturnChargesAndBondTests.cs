using System.Security.Claims;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Services;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class ReturnChargesAndBondTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.5, 5)]
    [InlineData(1, 10)]
    [InlineData(6, 60)]
    [InlineData(24, 240)]
    [InlineData(25.5, 255)]
    public void Fee_prorates_daily_rate_including_partial_hours(double hours, decimal expected)
    {
        var due = DateTimeOffset.Parse("2026-09-20T09:00:00+12:00");
        Assert.Equal(expected, ReturnChargePolicy.LateFee([new BookingItem { EndAt = due, DailyRate = 240 }], due.AddHours(hours).ToUniversalTime()));
    }

    [Fact]
    public void Fee_sums_only_overdue_items_and_rounds_to_cents()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(4.17m, ReturnChargePolicy.LateFee([
            new BookingItem { EndAt = now.AddHours(-1), DailyRate = 100 },
            new BookingItem { EndAt = now.AddHours(1), DailyRate = 240 }], now));
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData(0, 0)]
    [InlineData(25, 25)]
    public async Task Return_invoice_uses_automatic_fee_or_explicit_staff_override(int? fee, int expected)
    {
        await using var db = CreateDb();
        var (booking, context) = await Seed(db, BookingStatus.ConvertedToRental);
        var returnedAt = booking.Items.Single().EndAt.AddHours(6);
        var controller = new RentalOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = context };
        var request = new ReturnInspectionRequest("A-1", true, true, null, null, "Returned intact", null,
            "Customer", 0, null, fee, 0, 0, 0, 0, 0, null, PaymentMethod.Cash)
        {
            ReturnedAt = returnedAt, SignatureDataUrl = "data:image/png;base64,AA==",
            ChecklistItems = ["Checked"], EvidenceDataUrls = ["data:image/png;base64,AA=="],
        };
        Assert.IsType<OkObjectResult>(await controller.Return(booking.Id, request, TestContext.Current.CancellationToken));
        var invoice = await db.RentalInvoices.Include(x => x.Lines).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(240m + expected, invoice.Subtotal);
        Assert.Equal(decimal.Round((240m + expected) * .15m, 2), invoice.TaxAmount);
        Assert.Equal(expected, invoice.Lines.Where(x => x.Description == "Late return").Sum(x => x.UnitPrice));
        Assert.Equal(100m, booking.BondRefundAmount);
        Assert.Equal(returnedAt, (await db.RentalInspections.SingleAsync(TestContext.Current.CancellationToken)).CompletedAt);
    }

    [Theory]
    [InlineData(SystemRoles.RentalOfficer)]
    [InlineData(SystemRoles.BranchManager)]
    public async Task Responsible_staff_can_change_confirmed_bond(string role)
    {
        await using var db = CreateDb();
        var (booking, context) = await Seed(db, BookingStatus.Confirmed, role);
        var controller = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = context };
        Assert.IsType<OkObjectResult>(await controller.UpdateBond(booking.Id, new(250), TestContext.Current.CancellationToken));
        Assert.Equal(250m, booking.DepositRequired);
        Assert.Equal(BondStatus.AwaitingPayment, booking.BondStatus);
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateBond(booking.Id, new(99), TestContext.Current.CancellationToken));
        Assert.Equal(250m, booking.DepositRequired);
    }

    [Theory]
    [InlineData(BookingStatus.ConvertedToRental)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task Bond_cannot_be_changed_after_handover_or_cancellation(BookingStatus status)
    {
        await using var db = CreateDb();
        var (booking, context) = await Seed(db, status);
        var controller = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = context };
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateBond(booking.Id, new(250), TestContext.Current.CancellationToken));
        Assert.Equal(100m, booking.DepositRequired);
    }

    [Fact]
    public async Task Staff_outside_booking_scope_cannot_edit_bond_or_preview_fees()
    {
        await using var db = CreateDb();
        var (booking, context) = await Seed(db, BookingStatus.Confirmed);
        var user = await db.Users.SingleAsync(TestContext.Current.CancellationToken);
        user.BranchId = Guid.NewGuid();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var bookings = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = context };
        var rentals = new RentalOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = context };
        Assert.IsType<ForbidResult>(await bookings.UpdateBond(booking.Id, new(250), TestContext.Current.CancellationToken));
        Assert.IsType<ForbidResult>(await rentals.ReturnCharges(booking.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Signed_agreement_locks_bond()
    {
        await using var db = CreateDb();
        var (booking, context) = await Seed(db, BookingStatus.Confirmed);
        db.RentalAgreements.Add(new RentalAgreement { BookingId = booking.Id, BranchId = booking.BranchId,
            AgreementNumber = "AGR-1", TermsVersion = "1", TermsJson = "[]", CustomerSnapshotJson = "{}",
            AssetSnapshotJson = "{}", PricingSnapshotJson = "{}", CustomerSignatureName = "Customer", ApprovedByName = "Staff" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = context };
        Assert.IsType<BadRequestObjectResult>(await controller.UpdateBond(booking.Id, new(250), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null, 350)]
    [InlineData(0, 0)]
    [InlineData(200, 200)]
    public async Task Quote_conversion_applies_inherited_bond_unless_staff_overrides(int? requested, int expected)
    {
        await using var db = CreateDb();
        var (existing, context) = await Seed(db, BookingStatus.Completed);
        var asset = await db.Assets.SingleAsync(TestContext.Current.CancellationToken);
        asset.InheritBond = true;
        var division = await db.Divisions.SingleAsync(TestContext.Current.CancellationToken);
        division.DefaultBondAmount = 350;
        var quote = new SalesQuote { QuoteNumber = "Q-1", BranchId = existing.BranchId,
            DivisionId = division.Id, CustomerId = existing.CustomerId, Status = QuoteStatus.Accepted, Subtotal = 240, Total = 240 };
        db.SalesQuotes.Add(quote);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new CorporateOperationsController(db, new CurrentStaffScope(db), new EmailQueue(), null!) { ControllerContext = context };
        var start = DateTimeOffset.UtcNow.AddDays(1);
        Assert.IsType<OkObjectResult>(await controller.ConvertQuote(quote.Id,
            new ConvertQuoteRequest(asset.Id, start, start.AddDays(1), 240, requested, null), TestContext.Current.CancellationToken));
        var booking = await db.Bookings.SingleAsync(x => x.Id == quote.ConvertedBookingId, TestContext.Current.CancellationToken);
        Assert.Equal(expected, booking.DepositRequired);
        Assert.Equal(expected > 0 ? BondStatus.AwaitingPayment : BondStatus.NotRequired, booking.BondStatus);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Booking, ControllerContext)> Seed(ApplicationDbContext db, BookingStatus status, string role = SystemRoles.RentalOfficer)
    {
        var branch = new Branch { Code = "TEST", Name = "Test" };
        var division = new Division { Code = "TEST", Name = "Test" };
        var asset = new Asset { AssetNumber = "A-1", Name = "Test asset", BranchId = branch.Id, DivisionId = division.Id };
        var customer = new Customer { CustomerNumber = "C-1", Name = "Customer" };
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "staff", FullName = "Staff", BranchId = branch.Id, DivisionId = division.Id, IsActive = true };
        var due = DateTimeOffset.UtcNow.AddDays(-1);
        var booking = new Booking { BookingNumber = "BK-1", BranchId = branch.Id, CustomerId = customer.Id, Status = status,
            DepositRequired = 100, BondAmountHeld = 100, BondStatus = BondStatus.Held, TaxRate = 15,
            Items = [new BookingItem { AssetId = asset.Id, StartAt = due.AddDays(-1), EndAt = due, DailyRate = 240 }] };
        db.AddRange(branch, division, asset, customer, user, booking);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, role)], "test"));
        return (booking, new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } });
    }
}
