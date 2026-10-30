using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/rentals/{bookingId:guid}/lifecycle")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class RentalLifecycleController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(Guid bookingId, CancellationToken cancellationToken)
    {
        var access = await GetBooking(bookingId, cancellationToken);
        if (access.Result is not null) return access.Result;
        var booking = access.Booking!;
        return Ok(new
        {
            drivers = await db.AuthorizedDrivers.AsNoTracking().Where(x => x.BookingId == bookingId).OrderByDescending(x => x.IsPrimary).ToListAsync(cancellationToken),
            payments = await db.RentalPayments.AsNoTracking().Where(x => x.BookingId == bookingId).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken),
            notifications = await db.RentalNotifications.AsNoTracking().Where(x => x.BookingId == bookingId).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken),
            incidents = await db.RentalIncidents.AsNoTracking().Where(x => x.BookingId == bookingId).OrderByDescending(x => x.OccurredAt).ToListAsync(cancellationToken),
            invoice = await db.RentalInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.BookingId == bookingId, cancellationToken),
            addendums = await db.RentalAgreementAddendums.AsNoTracking().Where(x => x.RentalAgreement!.BookingId == bookingId).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken),
        });
    }

    [HttpPost("drivers")]
    public async Task<ActionResult> AddDriver(Guid bookingId, DriverRequest request, CancellationToken cancellationToken)
    {
        var access = await GetBooking(bookingId, cancellationToken); if (access.Result is not null) return access.Result;
        if (access.Booking!.Status is not (BookingStatus.Confirmed or BookingStatus.ConvertedToRental)) return BadRequest(new { message = "Drivers can only be added to confirmed or active hires." });
        if (await db.AuthorizedDrivers.AnyAsync(x => x.BookingId == bookingId && x.LicenceNumber == request.LicenceNumber.Trim(), cancellationToken)) return Conflict(new { message = "This licence is already recorded for the hire." });
        if (request.LicenceExpiry <= DateOnly.FromDateTime(DateTime.UtcNow)) return Validation("licenceExpiry", "The driver licence must not be expired.");
        if (!CREMS.Api.Services.DriverLicenceRules.ValidNumber(request.LicenceNumber) || !CREMS.Api.Services.DriverLicenceRules.ValidClasses(request.LicenceClass)) return BadRequest(new { message = "Enter a numeric licence number and select the applicable classes from 1 to 8." });
        var driver = new AuthorizedDriver { BookingId = bookingId, FullName = request.FullName.Trim(), LicenceNumber = request.LicenceNumber.Trim(), LicenceClass = CREMS.Api.Services.DriverLicenceRules.NormalizeClasses(request.LicenceClass!), LicenceExpiry = request.LicenceExpiry, Verified = request.Verified };
        db.AuthorizedDrivers.Add(driver); AuditWriter.Record(db, access.Scope!, "Authorized driver added", "Booking", bookingId, $"{driver.FullName} was added to {access.Booking!.BookingNumber}.", access.Booking.BranchId);
        await db.SaveChangesAsync(cancellationToken); return Ok(new { driver.Id, driver.BookingId, driver.FullName, driver.LicenceNumber, driver.LicenceClass, driver.LicenceExpiry, driver.Verified, driver.IsPrimary });
    }

    [HttpPost("payments")]
    public async Task<ActionResult> AddPayment(Guid bookingId, PaymentRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await CREMS.Api.Services.MaintenanceRules.BeginAsync(db, cancellationToken);
        var access = await GetBooking(bookingId, cancellationToken); if (access.Result is not null) return access.Result;
        var booking = access.Booking!;
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.ConvertedToRental or BookingStatus.Completed))
            return BadRequest(new { message = "Payments can only be received for confirmed, active or completed hires." });
        if (request.Type is PaymentType.BondRefund or PaymentType.Refund)
            return BadRequest(new { message = "Refunds must be recorded through the controlled return settlement process." });
        if (request.Method == PaymentMethod.PurchaseOrder || request.Amount != CREMS.Api.Services.RentalFinancials.Money(request.Amount))
            return BadRequest(new { message = "Select the actual payment method and enter an amount in dollars and cents." });
        var receipt = string.IsNullOrWhiteSpace(request.ReceiptNumber) ? $"RCPT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}" : request.ReceiptNumber.Trim();
        if (receipt.Length > 60) return BadRequest(new { message = "Keep the receipt reference to 60 characters or fewer." });
        if (await db.RentalPayments.AnyAsync(x => x.ReceiptNumber == receipt, cancellationToken))
            return Conflict(new { message = "This receipt reference is already recorded." });
        var invoice = await db.RentalInvoices.FirstOrDefaultAsync(x => x.BookingId == bookingId, cancellationToken);
        var bond = request.Type is PaymentType.Deposit or PaymentType.BondCollection;
        if (bond)
        {
            if (booking.Status == BookingStatus.Completed || booking.BondSettledAt.HasValue || request.Amount > Math.Max(0, booking.DepositRequired - booking.BondAmountHeld))
                return BadRequest(new { message = "Collect only the remaining required bond before settlement." });
            booking.BondAmountHeld += request.Amount;
            booking.BondStatus = booking.BondAmountHeld >= booking.DepositRequired ? BondStatus.Held : BondStatus.AwaitingPayment;
        }
        else
        {
            if (invoice?.Status is InvoiceStatus.Voided or InvoiceStatus.Draft)
                return BadRequest(new { message = "Issue or resolve the invoice before recording a hire payment." });
            var outstanding = invoice is not null ? invoice.BalanceDue : Math.Max(0, CREMS.Api.Services.RentalFinancials.Total(booking) - CREMS.Api.Services.RentalFinancials.Paid(booking));
            if (request.Amount > outstanding) return BadRequest(new { message = "The payment exceeds the outstanding hire balance. Record bonds separately." });
        }
        var payment = new RentalPayment { BookingId = bookingId, Type = request.Type, Method = request.Method, Amount = request.Amount, ReceiptNumber = receipt, Note = Clean(request.Note), RecordedByUserId = access.Scope!.UserId, RecordedByName = access.Scope.UserName };
        db.RentalPayments.Add(payment);
        if (!bond && invoice is not null)
        {
            db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = payment.Id, InvoiceId = invoice.Id, Amount = request.Amount, AllocatedByUserId = access.Scope.UserId });
            invoice.AmountPaid += request.Amount;
            invoice.BalanceDue = Math.Max(0, invoice.Total - invoice.AmountPaid);
            invoice.Status = invoice.BalanceDue == 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        }
        AuditWriter.Record(db, access.Scope, "Rental payment recorded", "Booking", bookingId, $"{payment.Type} of FJD {payment.Amount:0.00} recorded for {booking.BookingNumber}.", booking.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Ok(new { payment.Id, payment.BookingId, payment.Type, payment.Method, payment.Amount, payment.ReceiptNumber, payment.Status, payment.Note, payment.CreatedAt, payment.RecordedByName });
    }

    [HttpPost("incidents")]
    public async Task<ActionResult> AddIncident(Guid bookingId, IncidentRequest request, CancellationToken cancellationToken)
    {
        var access = await GetBooking(bookingId, cancellationToken); if (access.Result is not null) return access.Result;
        if (request.OccurredAt == default || request.OccurredAt > DateTimeOffset.UtcNow) return Validation("occurredAt", "Enter a valid incident time no later than now.");
        var incident = new RentalIncident { BookingId = bookingId, IncidentNumber = $"INC-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..7].ToUpperInvariant()}", Type = request.Type, OccurredAt = request.OccurredAt, Description = request.Description.Trim(), Location = Clean(request.Location), PoliceReference = Clean(request.PoliceReference), InsuranceClaimNumber = Clean(request.InsuranceClaimNumber), EstimatedCost = request.EstimatedCost, EvidenceJson = JsonSerializer.Serialize(request.EvidenceDataUrls ?? []) };
        db.RentalIncidents.Add(incident); AuditWriter.Record(db, access.Scope!, "Rental incident recorded", "Booking", bookingId, $"{incident.IncidentNumber} was opened for {access.Booking!.BookingNumber}.", access.Booking.BranchId);
        await db.SaveChangesAsync(cancellationToken); return Ok(new { incident.Id, incident.BookingId, incident.IncidentNumber, incident.Type, incident.OccurredAt, incident.Description, incident.EstimatedCost, incident.Status });
    }

    [HttpPost("notifications")]
    public async Task<ActionResult> QueueNotification(Guid bookingId, NotificationRequest request, CancellationToken cancellationToken)
    {
        var access = await GetBooking(bookingId, cancellationToken); if (access.Result is not null) return access.Result;
        var notification = new RentalNotification { BookingId = bookingId, Channel = request.Channel, Recipient = request.Recipient.Trim(), Subject = request.Subject.Trim(), Message = request.Message.Trim() };
        db.RentalNotifications.Add(notification); await db.SaveChangesAsync(cancellationToken); return Ok(new { notification.Id, notification.BookingId, notification.Channel, notification.Recipient, notification.Subject, notification.Message, notification.Status });
    }

    [HttpPost("addendums")]
    public async Task<ActionResult> AddAddendum(Guid bookingId, AddendumRequest request, CancellationToken cancellationToken)
    {
        var access = await GetBooking(bookingId, cancellationToken); if (access.Result is not null) return access.Result;
        var agreement = await db.RentalAgreements.FirstOrDefaultAsync(x => x.BookingId == bookingId, cancellationToken);
        if (agreement is null || agreement.Status is AgreementStatus.Draft or AgreementStatus.ReadyForPickup || access.Booking!.Status != BookingStatus.ConvertedToRental) return Validation("agreement", "A signed agreement is required before an addendum can be created.");
        if (request.NewDailyRate < 0 || request.NewEndAt.HasValue && access.Booking!.Items.Any(x => request.NewEndAt <= x.StartAt))
            return Validation("changes", "Enter a non-negative rate and an end time after the hire starts.");
        var addendum = new RentalAgreementAddendum { RentalAgreementId = agreement.Id, AddendumNumber = $"{agreement.AgreementNumber}-A{await db.RentalAgreementAddendums.CountAsync(x => x.RentalAgreementId == agreement.Id, cancellationToken) + 1}", Reason = request.Reason.Trim(), ChangesJson = JsonSerializer.Serialize(new { request.NewEndAt, request.NewDailyRate, request.Note }), CustomerSignatureName = request.CustomerSignatureName.Trim(), CustomerSignedAt = DateTimeOffset.UtcNow, ApprovedByUserId = access.Scope!.UserId, ApprovedByName = access.Scope.UserName };
        db.RentalAgreementAddendums.Add(addendum); AuditWriter.Record(db, access.Scope, "Rental addendum approved", "RentalAgreement", agreement.Id, $"{addendum.AddendumNumber} was signed without altering the original agreement.", access.Booking!.BranchId);
        await db.SaveChangesAsync(cancellationToken); return Ok(new { addendum.Id, addendum.RentalAgreementId, addendum.AddendumNumber, addendum.Reason, addendum.ChangesJson, addendum.CustomerSignatureName, addendum.CustomerSignedAt, addendum.ApprovedByName });
    }

    private async Task<(Booking? Booking, StaffDataScope? Scope, ActionResult? Result)> GetBooking(Guid id, CancellationToken token)
    {
        var booking = await db.Bookings.Include(x => x.Customer).Include(x => x.Items).ThenInclude(x => x.Asset).Include(x => x.Charges).Include(x => x.Payments).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, token);
        if (booking is null) return (null, null, NotFound());
        var scope = await staffScope.GetAsync(User);
        return scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId) ? (null, scope, Forbid()) : (booking, scope, null);
    }

    private BadRequestObjectResult Validation(string key, string message) => BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [key] = [message] }));
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record DriverRequest([Required] string FullName, [Required] string LicenceNumber, string? LicenceClass, DateOnly LicenceExpiry, bool Verified);
public sealed record PaymentRequest(PaymentType Type, PaymentMethod Method, [Range(0.01, 1000000)] decimal Amount, string? ReceiptNumber, string? Note);
public sealed record IncidentRequest(IncidentType Type, DateTimeOffset OccurredAt, [Required] string Description, string? Location, string? PoliceReference, string? InsuranceClaimNumber, [Range(0, 1000000)] decimal EstimatedCost, IReadOnlyList<string>? EvidenceDataUrls);
public sealed record NotificationRequest(NotificationChannel Channel, [Required] string Recipient, [Required] string Subject, [Required] string Message);
public sealed record AddendumRequest([Required] string Reason, DateTimeOffset? NewEndAt, decimal? NewDailyRate, string? Note, [Required] string CustomerSignatureName);
