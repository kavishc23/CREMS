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
        if (scope is null || !scope.HasBranchAccess(booking.BranchId)) return Forbid();
        return Ok(ToResponse(booking));
    }

    [HttpPost("{bookingId:guid}/handover")]
    public async Task<ActionResult> Handover(Guid bookingId, InspectionRequest request, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(item => item.Items).ThenInclude(item => item.Asset)
            .Include(item => item.Inspections).FirstOrDefaultAsync(item => item.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasBranchAccess(booking.BranchId)) return Forbid();
        if (booking.Status != BookingStatus.Confirmed)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["Only a confirmed booking can be handed over."] }));
        if (!await db.RentalAgreements.AnyAsync(item => item.BookingId == bookingId, cancellationToken))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["agreement"] = ["The customer rental agreement must be signed and approved before handover."] }));
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
        if (scope is null || !scope.HasBranchAccess(booking.BranchId)) return Forbid();
        if (booking.Status != BookingStatus.ConvertedToRental)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["Only an active rental can be returned."] }));

        booking.Inspections.Add(CreateInspection(booking.Id, InspectionType.Return, request, scope));
        booking.AdditionalCharges = request.AdditionalCharges;
        booking.AdditionalChargesDescription = Normalize(request.AdditionalChargesDescription);
        booking.Status = BookingStatus.Completed;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        var hasDamage = !string.IsNullOrWhiteSpace(request.DamageNotes);
        foreach (var item in booking.Items.Where(item => item.Asset is not null))
            item.Asset!.Status = hasDamage ? AssetStatus.Inspection : AssetStatus.Available;
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
    string? SignatureName);
public sealed record ReturnInspectionRequest(
    bool IdentificationVerified,
    bool DriverLicenceVerified,
    decimal? MeterReading,
    int? FuelLevelPercent,
    string? ConditionNotes,
    string? DamageNotes,
    string? SignatureName,
    [Range(0, 1000000)] decimal AdditionalCharges,
    string? AdditionalChargesDescription) : InspectionRequest(
        IdentificationVerified, DriverLicenceVerified, MeterReading, FuelLevelPercent,
        ConditionNotes, DamageNotes, SignatureName);
