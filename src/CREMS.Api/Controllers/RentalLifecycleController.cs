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
        if (request.LicenceExpiry <= DateOnly.FromDateTime(DateTime.UtcNow)) return Validation("licenceExpiry", "The driver licence must not be expired.");
        var driver = new AuthorizedDriver { BookingId = bookingId, FullName = request.FullName.Trim(), LicenceNumber = request.LicenceNumber.Trim(), LicenceClass = Clean(request.LicenceClass), LicenceExpiry = request.LicenceExpiry, Verified = request.Verified };
        db.AuthorizedDrivers.Add(driver); AuditWriter.Record(db, access.Scope!, "Authorized driver added", "Booking", bookingId, $"{driver.FullName} was added to {access.Booking!.BookingNumber}.", access.Booking.BranchId);
        await db.SaveChangesAsync(cancellationToken); return Ok(driver);
    }

    [HttpPost("payments")]
    public async Task<ActionResult> AddPayment(Guid bookingId, PaymentRequest request, CancellationToken cancellationToken)
    {
        var access = await GetBooking(bookingId, cancellationToken); if (access.Result is not null) return access.Result;
        if (request.Type is PaymentType.BondRefund or PaymentType.Refund)
            return BadRequest(new { message = "Refunds must be recorded through the controlled return settlement process." });
        if (request.Type is PaymentType.Deposit or PaymentType.BondCollection)
        {
            var booking = access.Booking!;
            if (booking.Status != BookingStatus.Confirmed && booking.Status != BookingStatus.ConvertedToRental)
                return BadRequest(new { message = "Confirm the booking before collecting its refundable bond." });
            if (booking.BondSettledAt.HasValue || request.Amount > Math.Max(0, booking.DepositRequired - booking.BondAmountHeld))
                return BadRequest(new { message = "Collect only the remaining required bond. A settled bond cannot receive further payments." });
            booking.BondAmountHeld += request.Amount;
            booking.BondStatus = booking.BondAmountHeld >= booking.DepositRequired ? BondStatus.Held : BondStatus.AwaitingPayment;
        }
        var payment = new RentalPayment { BookingId = bookingId, Type = request.Type, Method = request.Method, Amount = request.Amount, ReceiptNumber = string.IsNullOrWhiteSpace(request.ReceiptNumber) ? $"RCPT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}" : request.ReceiptNumber.Trim(), Note = Clean(request.Note), RecordedByUserId = access.Scope!.UserId, RecordedByName = access.Scope.UserName };
        db.RentalPayments.Add(payment); AuditWriter.Record(db, access.Scope, "Rental payment recorded", "Booking", bookingId, $"{payment.Type} of FJD {payment.Amount:0.00} recorded for {access.Booking!.BookingNumber}.", access.Booking.BranchId);
        await db.SaveChangesAsync(cancellationToken); return Ok(payment);
    }

    [HttpPost("incidents")]
    public async Task<ActionResult> AddIncident(Guid bookingId, IncidentRequest request, CancellationToken cancellationToken)
    {
        var access = await GetBooking(bookingId, cancellationToken); if (access.Result is not null) return access.Result;
        var incident = new RentalIncident { BookingId = bookingId, IncidentNumber = $"INC-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..7].ToUpperInvariant()}", Type = request.Type, OccurredAt = request.OccurredAt, Description = request.Description.Trim(), Location = Clean(request.Location), PoliceReference = Clean(request.PoliceReference), InsuranceClaimNumber = Clean(request.InsuranceClaimNumber), EstimatedCost = request.EstimatedCost, EvidenceJson = JsonSerializer.Serialize(request.EvidenceDataUrls ?? []) };
        db.RentalIncidents.Add(incident); AuditWriter.Record(db, access.Scope!, "Rental incident recorded", "Booking", bookingId, $"{incident.IncidentNumber} was opened for {access.Booking!.BookingNumber}.", access.Booking.BranchId);
        await db.SaveChangesAsync(cancellationToken); return Ok(incident);
    }

    [HttpPost("notifications")]
    public async Task<ActionResult> QueueNotification(Guid bookingId, NotificationRequest request, CancellationToken cancellationToken)
    {
        var access = await GetBooking(bookingId, cancellationToken); if (access.Result is not null) return access.Result;
        var notification = new RentalNotification { BookingId = bookingId, Channel = request.Channel, Recipient = request.Recipient.Trim(), Subject = request.Subject.Trim(), Message = request.Message.Trim() };
        db.RentalNotifications.Add(notification); await db.SaveChangesAsync(cancellationToken); return Ok(notification);
    }

    [HttpPost("addendums")]
    public async Task<ActionResult> AddAddendum(Guid bookingId, AddendumRequest request, CancellationToken cancellationToken)
    {
        var access = await GetBooking(bookingId, cancellationToken); if (access.Result is not null) return access.Result;
        var agreement = await db.RentalAgreements.FirstOrDefaultAsync(x => x.BookingId == bookingId, cancellationToken);
        if (agreement is null) return Validation("agreement", "A signed agreement is required before an addendum can be created.");
        var addendum = new RentalAgreementAddendum { RentalAgreementId = agreement.Id, AddendumNumber = $"{agreement.AgreementNumber}-A{await db.RentalAgreementAddendums.CountAsync(x => x.RentalAgreementId == agreement.Id, cancellationToken) + 1}", Reason = request.Reason.Trim(), ChangesJson = JsonSerializer.Serialize(new { request.NewEndAt, request.NewDailyRate, request.Note }), CustomerSignatureName = request.CustomerSignatureName.Trim(), CustomerSignedAt = DateTimeOffset.UtcNow, ApprovedByUserId = access.Scope!.UserId, ApprovedByName = access.Scope.UserName };
        db.RentalAgreementAddendums.Add(addendum); AuditWriter.Record(db, access.Scope, "Rental addendum approved", "RentalAgreement", agreement.Id, $"{addendum.AddendumNumber} was signed without altering the original agreement.", access.Booking!.BranchId);
        await db.SaveChangesAsync(cancellationToken); return Ok(addendum);
    }

    private async Task<(Booking? Booking, StaffDataScope? Scope, ActionResult? Result)> GetBooking(Guid id, CancellationToken token)
    {
        var booking = await db.Bookings.Include(x => x.Customer).Include(x => x.Items).ThenInclude(x => x.Asset).FirstOrDefaultAsync(x => x.Id == id, token);
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
