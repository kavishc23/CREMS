using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/rentals")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class RentalOperationsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("{bookingId:guid}")]
    public async Task<ActionResult> Get(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().Include(item => item.Items).ThenInclude(item => item.Asset)
            .Include(item => item.Inspections).FirstOrDefaultAsync(item => item.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        return Ok(ToResponse(booking));
    }

    [HttpPost("{bookingId:guid}/handover")]
    public async Task<ActionResult> Handover(Guid bookingId, InspectionRequest request, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(item => item.Customer).Include(item => item.Items).ThenInclude(item => item.Asset)
            .Include(item => item.Inspections).FirstOrDefaultAsync(item => item.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status != BookingStatus.Confirmed)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["Only a confirmed booking can be handed over."] }));
        if (!await db.RentalAgreements.AnyAsync(item => item.BookingId == bookingId, cancellationToken))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["agreement"] = ["The customer rental agreement must be signed and approved before handover."] }));
        if (!await db.AuthorizedDrivers.AnyAsync(item => item.BookingId == bookingId && item.Verified && item.LicenceExpiry > DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["driver"] = ["At least one authorized driver with a current verified licence is required."] }));
        var recordedPayments = await db.RentalPayments.Where(item => item.BookingId == bookingId && item.Status == PaymentStatus.Recorded).SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0;
        if (!request.PaymentVerified || recordedPayments < booking.DepositRequired)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["payment"] = [$"The required deposit of FJD {booking.DepositRequired:0.00} must be recorded and verified before handover."] }));
        if (!request.IdentificationVerified || !request.DriverLicenceVerified || string.IsNullOrWhiteSpace(request.SignatureName))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["inspection"] = ["Identification, driver licence and customer signature are required."] }));

        booking.Inspections.Add(CreateInspection(booking.Id, InspectionType.Handover, request, scope));
        booking.Status = BookingStatus.ConvertedToRental;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var item in booking.Items.Where(item => item.Asset is not null)) item.Asset!.Status = AssetStatus.Rented;
        AuditWriter.Record(db, scope, "Rental handed over", "Booking", booking.Id,
            $"{booking.BookingNumber} was handed over to the customer.", booking.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(booking));
    }

    [HttpPost("{bookingId:guid}/return")]
    public async Task<ActionResult> Return(Guid bookingId, ReturnInspectionRequest request, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(item => item.Items).ThenInclude(item => item.Asset)
            .Include(item => item.Inspections).FirstOrDefaultAsync(item => item.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status != BookingStatus.ConvertedToRental)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["Only an active rental can be returned."] }));

        booking.Inspections.Add(CreateInspection(booking.Id, InspectionType.Return, request, scope));
        booking.AdditionalCharges = request.AdditionalCharges + request.LateFee + request.ExcessUsageCharge + request.RefuellingCharge + request.CleaningCharge + request.DamageCharge;
        booking.AdditionalChargesDescription = Normalize(request.AdditionalChargesDescription);
        booking.Status = BookingStatus.Completed;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        var hasDamage = !string.IsNullOrWhiteSpace(request.DamageNotes);
        foreach (var item in booking.Items.Where(item => item.Asset is not null))
            item.Asset!.Status = hasDamage ? AssetStatus.Inspection : AssetStatus.Available;
        var rentalSubtotal = booking.Items.Sum(item => item.DailyRate * Math.Max(1, (decimal)Math.Ceiling((item.EndAt - item.StartAt).TotalDays)));
        var invoiceSubtotal = Math.Max(0, rentalSubtotal - booking.DiscountAmount + booking.AdditionalCharges);
        var invoiceTax = invoiceSubtotal * booking.TaxRate / 100m;
        var paid = await db.RentalPayments.Where(x => x.BookingId == bookingId && x.Status == PaymentStatus.Recorded).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
        var invoice = new RentalInvoice
        {
            BookingId = booking.Id, InvoiceNumber = $"INV-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            LineItemsJson = System.Text.Json.JsonSerializer.Serialize(new[] { new { description = "Rental charges", amount = rentalSubtotal }, new { description = "Discount", amount = -booking.DiscountAmount }, new { description = "Late return", amount = request.LateFee }, new { description = "Excess kilometres / hours", amount = request.ExcessUsageCharge }, new { description = "Refuelling", amount = request.RefuellingCharge }, new { description = "Cleaning", amount = request.CleaningCharge }, new { description = "Damage", amount = request.DamageCharge }, new { description = request.AdditionalChargesDescription ?? "Other charges", amount = request.AdditionalCharges } }),
            Subtotal = invoiceSubtotal, TaxAmount = invoiceTax, Total = invoiceSubtotal + invoiceTax,
            AmountPaid = paid, BalanceDue = Math.Max(0, invoiceSubtotal + invoiceTax - paid),
            Status = paid >= invoiceSubtotal + invoiceTax ? InvoiceStatus.Paid : paid > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Issued,
        };
        db.RentalInvoices.Add(invoice);
        if (!string.IsNullOrWhiteSpace(booking.Customer?.Email)) db.RentalNotifications.Add(new RentalNotification { BookingId = booking.Id, Channel = NotificationChannel.Email, Recipient = booking.Customer.Email, Subject = $"Final invoice {invoice.InvoiceNumber}", Message = $"Your rental {booking.BookingNumber} has been returned. Final total: FJD {invoice.Total:0.00}; balance due: FJD {invoice.BalanceDue:0.00}." });
        var agreement = await db.RentalAgreements.FirstOrDefaultAsync(x => x.BookingId == bookingId, cancellationToken);
        if (agreement is not null) agreement.Status = AgreementStatus.Completed;
        AuditWriter.Record(db, scope, "Rental returned", "Booking", booking.Id,
            $"{booking.BookingNumber} was returned{(hasDamage ? " with damage requiring inspection" : string.Empty)}.", booking.BranchId,
            null, $"Additional charges: {request.AdditionalCharges:0.00}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(booking));
    }

    private static RentalInspection CreateInspection(Guid bookingId, InspectionType type,
        InspectionRequest request, StaffDataScope scope) => new()
    {
        BookingId = bookingId, Type = type, IdentificationVerified = request.IdentificationVerified,
        DriverLicenceVerified = request.DriverLicenceVerified, MeterReading = request.MeterReading,
        FuelLevelPercent = request.FuelLevelPercent, ConditionNotes = Normalize(request.ConditionNotes),
        DamageNotes = Normalize(request.DamageNotes), SignatureName = Normalize(request.SignatureName),
        SignatureDataUrl = request.SignatureDataUrl, PaymentVerified = request.PaymentVerified,
        EvidenceJson = System.Text.Json.JsonSerializer.Serialize(new { photos = request.EvidenceDataUrls ?? [], damageZones = request.DamageZones ?? [] }),
        CompletedByUserId = scope.UserId, CompletedByName = scope.UserName,
    };

    private static object ToResponse(Booking booking)
    {
        var subtotal = booking.Items.Sum(item => item.DailyRate * Math.Max(1,
            (decimal)Math.Ceiling((item.EndAt - item.StartAt).TotalDays)));
        var taxable = Math.Max(0, subtotal - booking.DiscountAmount + booking.AdditionalCharges);
        return new { booking.Id, booking.BookingNumber, booking.Status, booking.DepositRequired,
            booking.DiscountAmount, booking.TaxRate, booking.AdditionalCharges, booking.AdditionalChargesDescription,
            Subtotal = subtotal, TaxAmount = taxable * booking.TaxRate / 100m, Total = taxable * (1 + booking.TaxRate / 100m),
            Inspections = booking.Inspections.OrderBy(item => item.CompletedAt).Select(item => new { item.Id, item.Type,
                item.IdentificationVerified, item.DriverLicenceVerified, item.MeterReading, item.FuelLevelPercent,
                item.ConditionNotes, item.DamageNotes, item.SignatureName, item.CompletedByName, item.CompletedAt }) };
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record InspectionRequest(
    bool IdentificationVerified,
    bool DriverLicenceVerified,
    [Range(0, 10000000)] decimal? MeterReading,
    [Range(0, 100)] int? FuelLevelPercent,
    string? ConditionNotes,
    string? DamageNotes,
    string? SignatureName)
{
    public string? SignatureDataUrl { get; init; }
    public bool PaymentVerified { get; init; }
    public IReadOnlyList<string>? EvidenceDataUrls { get; init; }
    public IReadOnlyList<string>? DamageZones { get; init; }
}
public sealed record ReturnInspectionRequest(
    bool IdentificationVerified,
    bool DriverLicenceVerified,
    decimal? MeterReading,
    int? FuelLevelPercent,
    string? ConditionNotes,
    string? DamageNotes,
    string? SignatureName,
    [Range(0, 1000000)] decimal AdditionalCharges,
    string? AdditionalChargesDescription,
    [Range(0, 1000000)] decimal LateFee,
    [Range(0, 1000000)] decimal ExcessUsageCharge,
    [Range(0, 1000000)] decimal RefuellingCharge,
    [Range(0, 1000000)] decimal CleaningCharge,
    [Range(0, 1000000)] decimal DamageCharge) : InspectionRequest(
        IdentificationVerified, DriverLicenceVerified, MeterReading, FuelLevelPercent,
        ConditionNotes, DamageNotes, SignatureName);
