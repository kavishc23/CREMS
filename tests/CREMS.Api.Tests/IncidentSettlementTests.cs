using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class IncidentSettlementTests
{
    [Fact]
    public async Task Assigned_rental_officer_can_record_follow_up_for_their_hire()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        asset.DivisionId = Guid.NewGuid();
        var booking = new Booking
        {
            BookingNumber = "INC-ASSIGNMENT", BranchId = asset.BranchId, Status = BookingStatus.Completed,
            IncidentClosedAt = DateTimeOffset.UtcNow,
            Customer = new Customer { CustomerNumber = "INC-ASSIGNMENT-CUSTOMER", Name = "Test customer" },
            Items = [new BookingItem { AssetId = asset.Id }]
        };
        var officer = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Rental officer", BranchId = asset.BranchId, DivisionId = asset.DivisionId };
        var outside = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Other branch officer", BranchId = Guid.NewGuid(), DivisionId = asset.DivisionId };
        var role = new Microsoft.AspNetCore.Identity.IdentityRole<Guid> { Id = Guid.NewGuid(), Name = SystemRoles.RentalOfficer };
        db.AddRange(booking, officer, outside, role);
        db.UserRoles.AddRange(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = officer.Id, RoleId = role.Id },
            new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = outside.Id, RoleId = role.Id });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        var controller = new IncidentSettlementController(db, new CurrentStaffScope(db), new EmailQueue()) { ControllerContext = staff.ControllerContext };
        var today = MaintenanceRules.LocalDate(DateTimeOffset.UtcNow);
        var request = new IncidentSettlementRequest(0, false, today, [], [], null, null, false, false, 0, null,
            PaymentMethod.BankTransfer, null, false, false, null, AssignedUserId: outside.Id);
        Assert.IsType<BadRequestObjectResult>(await controller.Save(booking.Id, request, token));
        Assert.IsType<OkObjectResult>(await controller.Save(booking.Id, request with { AssignedUserId = officer.Id }, token));

        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new(System.Security.Claims.ClaimTypes.NameIdentifier, officer.Id.ToString())], "test"));
        var assigned = new IncidentSettlementController(db, new CurrentStaffScope(db), new EmailQueue())
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = principal } }
        };
        Assert.IsType<OkObjectResult>(await assigned.Get(booking.Id, token));
        Assert.IsType<NoContentResult>(await assigned.UpdateFollowUp(booking.Id, new(1, today.AddDays(1), "Awaiting repair estimate."), token));
        var settlement = await db.IncidentSettlements.SingleAsync(token);
        Assert.Equal("Awaiting repair estimate.", JsonSerializer.Deserialize<IncidentSettlementState>(settlement.DetailsJson)!.FollowUpNotes);
        Assert.Equal(officer.Id, (await db.ManagementTasks.SingleAsync(token)).AssignedUserId);
        Assert.Empty(db.RentalInvoices);
    }

    [Fact]
    public async Task Issues_hire_balance_then_adds_approved_cost_and_applies_bond_without_return()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var start = DateTimeOffset.UtcNow.AddDays(-2);
        var booking = new Booking
        {
            BookingNumber = "INC-SETTLEMENT", BranchId = asset.BranchId, Status = BookingStatus.Completed,
            IncidentClosedAt = DateTimeOffset.UtcNow, BondAmountHeld = 100, BondStatus = BondStatus.Held,
            Customer = new Customer { CustomerNumber = "INC-ACCOUNT", Name = "Test customer", Email = "customer@example.invalid" },
            Items = [new BookingItem { AssetId = asset.Id, StartAt = start, EndAt = start.AddDays(2), DailyRate = 100, HireIsTaxable = false }],
            Payments = [new RentalPayment { Amount = 50, Type = PaymentType.RentalCharge, ReceiptNumber = "FIRST-INSTALMENT",
                RecordedByName = "Test operator" }]
        };
        db.Bookings.Add(booking);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        var scope = new CurrentStaffScope(db);
        var controller = new IncidentSettlementController(db, scope, new EmailQueue()) { ControllerContext = staff.ControllerContext };
        var lifecycle = new RentalLifecycleController(db, scope) { ControllerContext = staff.ControllerContext };
        var finance = new FinanceController(db, scope) { ControllerContext = staff.ControllerContext };
        var today = MaintenanceRules.LocalDate(DateTimeOffset.UtcNow);
        var cost = new IncidentCostDecision(Guid.NewGuid(), "Repair", 80, "Pending", "", "", false, false);
        var request = new IncidentSettlementRequest(0, true, today, [cost], [], "Signed early termination clause",
            today.AddDays(7), true, false, 0, null, PaymentMethod.BankTransfer, null, false, false, null,
            InvoiceCredit: 10, InvoiceCreditReason: "Agreed adjustment to the hire balance");

        Assert.IsType<BadRequestObjectResult>(await controller.Save(booking.Id, request with { DueDate = null }, token));
        Assert.IsType<OkObjectResult>(await controller.Save(booking.Id, request, token));
        var invoice = await db.RentalInvoices.Include(x => x.Lines).SingleAsync(token);
        Assert.Equal(190, invoice.Total);
        Assert.Equal(140, invoice.BalanceDue);
        Assert.Single(invoice.Lines);
        Assert.Null(booking.BondSettledAt);
        Assert.IsType<ConflictObjectResult>(await controller.Save(booking.Id, request, token));
        Assert.IsType<ConflictObjectResult>(await controller.Save(booking.Id,
            request with { Revision = 1, InvoiceCredit = 0, InvoiceCreditReason = null }, token));
        Assert.Single(await db.OutboundEmails.ToListAsync(token));
        Assert.IsType<BadRequestObjectResult>(await controller.Save(booking.Id,
            request with { Revision = 1, IssueInvoice = false, CloseCase = true, Outcome = "Attempt to close early" }, token));

        request = request with
        {
            Revision = 1, Costs = [cost with { Decision = "Customer", Responsibility = "Hirer", Evidence = "Repair quote R-12 and signed agreement" }],
            SettleBond = true, BondCredit = 60, BondReason = "Approved damage charge",
            BondRefundReference = "ACTUAL-REFUND", InvoiceCredit = 0, InvoiceCreditReason = null
        };
        Assert.IsType<OkObjectResult>(await controller.Save(booking.Id, request, token));
        Assert.Equal(270, invoice.Total);
        Assert.Equal(110, invoice.AmountPaid);
        Assert.Equal(160, invoice.BalanceDue);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Single(booking.Charges);
        Assert.Equal(40, booking.BondRefundAmount);
        Assert.Equal(BondStatus.PartiallyRefunded, booking.BondStatus);
        Assert.Empty(await db.RentalInspections.ToListAsync(token));
        Assert.Equal(2, await db.OutboundEmails.CountAsync(token));

        var settlement = await db.IncidentSettlements.SingleAsync(token);
        var billedCosts = JsonSerializer.Deserialize<IncidentSettlementState>(settlement.DetailsJson)!.Costs;
        Assert.IsType<BadRequestObjectResult>(await controller.Save(booking.Id,
            request with { Revision = 2, Costs = [], SettleBond = false }, token));
        Assert.IsType<OkObjectResult>(await lifecycle.AddPayment(booking.Id,
            new PaymentRequest(PaymentType.RentalCharge, PaymentMethod.BankTransfer, 140, "SECOND-INSTALMENT", null), token));
        Assert.IsType<OkObjectResult>(await finance.Credit(invoice.Id, new CreditNoteRequest("Approved settlement adjustment", 20), token));
        Assert.Equal(0, invoice.BalanceDue);
        var revised = request with { Revision = 2, Costs = billedCosts, SettleBond = false, DueDate = null };
        Assert.IsType<OkObjectResult>(await controller.Save(booking.Id, revised, token));
        Assert.Null(settlement.PaymentDueDate);
        var paidNotice = await db.OutboundEmails.SingleAsync(x => x.TextBody!.Contains("No payment is due."), token);
        Assert.DoesNotContain("Payment due:", paidNotice.TextBody);
        Assert.IsType<ConflictObjectResult>(await controller.Save(booking.Id, revised with { Revision = 3, DueDate = today }, token));
        Assert.Equal(3, await db.OutboundEmails.CountAsync(token));
        Assert.Single(await db.RentalInvoices.ToListAsync(token));
        Assert.IsType<OkObjectResult>(await controller.Save(booking.Id,
            request with { Revision = 3, Costs = billedCosts, IssueInvoice = false, SettleBond = false, CloseCase = true,
                Outcome = "Invoice paid and approved credit applied" }, token));
        var task = await db.ManagementTasks.SingleAsync(x => x.SourceEntityId == booking.Id, token);
        Assert.True(task.IsCompleted);
        Assert.NotNull(task.AssignedUserId);
        Assert.NotNull(settlement.ClosedAt);
    }
}
