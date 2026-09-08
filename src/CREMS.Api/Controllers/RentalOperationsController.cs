using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/rentals")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class RentalOperationsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("work-queue")]
    public async Task<ActionResult<RentalWorkQueueResponse>> GetWorkQueue(
        [FromQuery] string queue = "PickupToday", [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100);
        var now = DateTimeOffset.UtcNow; var todayStart = new DateTimeOffset(now.ToOffset(TimeSpan.FromHours(12)).Date, TimeSpan.FromHours(12));
        var todayEnd = todayStart.AddDays(1); var recent = now.AddDays(-14);
        var baseQuery = db.Bookings.AsNoTracking().Where(x => x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.ConvertedToRental || x.Status == BookingStatus.Completed);
        if (!scope.IsAdministrator) baseQuery = baseQuery.Where(x => x.BranchId == scope.BranchId && x.Items.Any(i => i.Asset!.DivisionId == scope.DivisionId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQuery = baseQuery.Where(x => x.BookingNumber.Contains(term) || x.Customer!.Name.Contains(term) ||
                (x.Customer.Phone != null && x.Customer.Phone.Contains(term)) || x.Items.Any(i => i.Asset!.Name.Contains(term) || i.Asset.AssetNumber.Contains(term)));
        }
        IQueryable<Booking> selected = queue switch
        {
            "OnHire" => baseQuery.Where(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt >= todayEnd)),
            "DueToday" => baseQuery.Where(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt >= todayStart && i.EndAt < todayEnd)),
            "Overdue" => baseQuery.Where(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt < todayStart)),
            "ReturnInProgress" => baseQuery.Where(x => x.Status == BookingStatus.ConvertedToRental && x.Inspections.Any(i => i.Type == InspectionType.Return)),
            "RecentlyCompleted" => baseQuery.Where(x => x.Status == BookingStatus.Completed && x.UpdatedAt >= recent),
            _ => baseQuery.Where(x => x.Status == BookingStatus.Confirmed && x.Items.Any(i => i.StartAt < todayEnd)),
        };
        var counts = new RentalQueueCounts(
            await baseQuery.CountAsync(x => x.Status == BookingStatus.Confirmed && x.Items.Any(i => i.StartAt < todayEnd), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt >= todayEnd), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt >= todayStart && i.EndAt < todayEnd), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt < todayStart), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.ConvertedToRental && x.Inspections.Any(i => i.Type == InspectionType.Return), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.Completed && x.UpdatedAt >= recent, cancellationToken));
        var total = await selected.CountAsync(cancellationToken);
        var rows = await selected.OrderBy(x => x.Items.Select(i => i.StartAt).FirstOrDefault()).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new RentalWorkQueueRow(x.Id, x.BookingNumber, x.Status.ToString(), x.Customer!.Name, x.Customer.Phone,
                x.Branch!.Name, x.Items.Select(i => i.Asset!.AssetNumber).FirstOrDefault(), x.Items.Select(i => i.Asset!.Name).FirstOrDefault(),
                x.Items.Select(i => (DateTimeOffset?)i.StartAt).FirstOrDefault(), x.Items.Select(i => (DateTimeOffset?)i.EndAt).FirstOrDefault(),
                x.RentalAgreement != null, x.RentalAgreement != null ? x.RentalAgreement.Status.ToString() : "Not prepared",
                x.Payments.Where(p => p.Status == PaymentStatus.Recorded && (p.Type == PaymentType.RentalCharge || p.Type == PaymentType.AdditionalCharge)).Sum(p => (decimal?)p.Amount) ?? 0, x.DepositRequired,
                x.BondStatus.ToString(), x.BondAmountHeld, x.BondDeductionAmount, x.BondRefundAmount,
                x.Inspections.Any(i => i.Type == InspectionType.Handover), x.Inspections.Any(i => i.Type == InspectionType.Return),
                x.Items.Any(i => i.EndAt < now) ? "Return is overdue" : x.Customer.IsBlocked ? "Customer account is blocked" : null))
            .ToListAsync(cancellationToken);
        return Ok(new RentalWorkQueueResponse(rows, counts, page, pageSize, total));
    }

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
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["payment"] = [$"The required refundable bond of FJD {booking.DepositRequired:0.00} must be recorded and verified before handover."] }));
        if (!request.IdentificationVerified || !request.DriverLicenceVerified || string.IsNullOrWhiteSpace(request.SignatureName))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["inspection"] = ["Identification, driver licence and customer signature are required."] }));

        booking.Inspections.Add(CreateInspection(booking.Id, InspectionType.Handover, request, scope));
        booking.Status = BookingStatus.ConvertedToRental;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var item in booking.Items.Where(item => item.Asset is not null)) { var asset=item.Asset!;var from=asset.Status;asset.Status=AssetStatus.Rented;db.AssetLifecycleEvents.Add(new AssetLifecycleEvent{AssetId=asset.Id,BookingId=booking.Id,Type=AssetLifecycleEventType.CheckedOut,FromStatus=from,ToStatus=asset.Status,MeterReading=request.MeterReading,Notes=$"Checked out on {booking.BookingNumber}",RecordedByUserId=scope.UserId,RecordedByName=scope.UserName});if(request.MeterReading.HasValue)db.AssetMeterReadings.Add(new AssetMeterReading{AssetId=asset.Id,BookingId=booking.Id,Type=asset.MeterUnit?.Contains("hour",StringComparison.OrdinalIgnoreCase)==true?MeterType.EngineHours:MeterType.Odometer,Unit=asset.MeterUnit??"unit",Reading=request.MeterReading.Value,FuelPercent=request.FuelLevelPercent,Source=MeterReadingSource.PreHireInspection,RecordedByUserId=scope.UserId});}
        AuditWriter.Record(db, scope, "Rental handed over", "Booking", booking.Id,
            $"{booking.BookingNumber} was handed over to the customer.", booking.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(booking));
    }

    [HttpPost("{bookingId:guid}/return")]
    public async Task<ActionResult> Return(Guid bookingId, ReturnInspectionRequest request, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(item => item.Customer).Include(item => item.Items).ThenInclude(item => item.Asset)
            .Include(item => item.Inspections).Include(item => item.Payments).FirstOrDefaultAsync(item => item.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status != BookingStatus.ConvertedToRental)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["Only an active rental can be returned."] }));
        if (request.BondDeductionAmount < 0 || request.BondDeductionAmount > booking.BondAmountHeld)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["bond"] = ["The bond deduction must be between zero and the amount held."] }));
        if (request.BondDeductionAmount > 0 && string.IsNullOrWhiteSpace(request.BondDeductionReason))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["bondReason"] = ["Give a clear reason for every bond deduction."] }));

        booking.Inspections.Add(CreateInspection(booking.Id, InspectionType.Return, request, scope));
        booking.AdditionalCharges = request.AdditionalCharges + request.LateFee + request.ExcessUsageCharge + request.RefuellingCharge + request.CleaningCharge + request.DamageCharge;
        booking.AdditionalChargesDescription = Normalize(request.AdditionalChargesDescription);
        booking.Status = BookingStatus.Completed;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        booking.BondDeductionAmount = request.BondDeductionAmount;
        booking.BondDeductionReason = Normalize(request.BondDeductionReason);
        booking.BondRefundAmount = Math.Max(0, booking.BondAmountHeld - booking.BondDeductionAmount);
        booking.BondSettledAt = DateTimeOffset.UtcNow;
        booking.BondStatus = booking.BondAmountHeld <= 0 ? BondStatus.NotRequired
            : booking.BondRefundAmount == booking.BondAmountHeld ? BondStatus.Refunded
            : booking.BondRefundAmount > 0 ? BondStatus.PartiallyRefunded : BondStatus.Retained;
        if (booking.BondRefundAmount > 0)
            booking.Payments.Add(new RentalPayment { BookingId = booking.Id, Type = PaymentType.BondRefund,
                Method = request.BondRefundMethod, Amount = booking.BondRefundAmount,
                ReceiptNumber = $"BRF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                Note = booking.BondDeductionAmount > 0 ? $"Bond refund after deduction: {booking.BondDeductionReason}" : "Full refundable bond returned.",
                RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        var hasDamage = !string.IsNullOrWhiteSpace(request.DamageNotes);
        foreach (var item in booking.Items.Where(item => item.Asset is not null))
        {
            var asset=item.Asset!;var from=asset.Status;asset.Status=hasDamage?AssetStatus.Inspection:AssetStatus.Available;
            db.AssetLifecycleEvents.Add(new AssetLifecycleEvent{AssetId=asset.Id,BookingId=booking.Id,Type=hasDamage?AssetLifecycleEventType.DamageReported:AssetLifecycleEventType.PostHireInspection,FromStatus=from,ToStatus=asset.Status,MeterReading=request.MeterReading,Notes=Normalize(request.DamageNotes)??$"Returned on {booking.BookingNumber}",RecordedByUserId=scope.UserId,RecordedByName=scope.UserName});
            if(request.MeterReading.HasValue){asset.CurrentMeterReading=request.MeterReading;db.AssetMeterReadings.Add(new AssetMeterReading{AssetId=asset.Id,BookingId=booking.Id,Type=asset.MeterUnit?.Contains("hour",StringComparison.OrdinalIgnoreCase)==true?MeterType.EngineHours:MeterType.Odometer,Unit=asset.MeterUnit??"unit",Reading=request.MeterReading.Value,FuelPercent=request.FuelLevelPercent,Source=MeterReadingSource.PostHireInspection,RecordedByUserId=scope.UserId});}
            if (hasDamage && !await db.MaintenanceJobs.AnyAsync(x => x.AssetId == asset.Id && x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled, cancellationToken))
                db.MaintenanceJobs.Add(new MaintenanceJob { JobNumber = $"MNT-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}", AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = "Post-hire damage assessment", FaultDescription = request.DamageNotes!.Trim(), Description = $"Automatically referred from return of {booking.BookingNumber}.", Priority = MaintenancePriority.High, Status = MaintenanceStatus.Open, MeterReading = request.MeterReading });
        }
        var rentalSubtotal = booking.Items.Sum(item => item.DailyRate * Math.Max(1, (decimal)Math.Ceiling((item.EndAt - item.StartAt).TotalDays)));
        var invoiceSubtotal = Math.Max(0, rentalSubtotal - booking.DiscountAmount + booking.AdditionalCharges);
        var invoiceTax = invoiceSubtotal * booking.TaxRate / 100m;
        var paid = await db.RentalPayments.Where(x => x.BookingId == bookingId && x.Status == PaymentStatus.Recorded &&
            (x.Type == PaymentType.RentalCharge || x.Type == PaymentType.AdditionalCharge)).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
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
            bond = new { required = booking.DepositRequired, held = booking.BondAmountHeld, deduction = booking.BondDeductionAmount, booking.BondDeductionReason, refund = booking.BondRefundAmount, status = booking.BondStatus.ToString(), booking.BondSettledAt },
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
    [Range(0, 1000000)] decimal DamageCharge,
    [Range(0, 1000000)] decimal BondDeductionAmount,
    string? BondDeductionReason,
    PaymentMethod BondRefundMethod) : InspectionRequest(
        IdentificationVerified, DriverLicenceVerified, MeterReading, FuelLevelPercent,
        ConditionNotes, DamageNotes, SignatureName);
public sealed record RentalQueueCounts(int PickupToday, int OnHire, int DueToday, int Overdue, int ReturnInProgress, int RecentlyCompleted);
public sealed record RentalWorkQueueResponse(IReadOnlyList<RentalWorkQueueRow> Items, RentalQueueCounts Counts, int Page, int PageSize, int Total);
public sealed record RentalWorkQueueRow(Guid Id, string BookingNumber, string Status, string CustomerName, string? CustomerPhone,
    string BranchName, string? AssetNumber, string? AssetName, DateTimeOffset? StartAt, DateTimeOffset? EndAt,
    bool HasAgreement, string AgreementStatus, decimal AmountPaid, decimal DepositRequired,
    string BondStatus, decimal BondAmountHeld, decimal BondDeductionAmount, decimal BondRefundAmount,
    bool PreHireInspectionComplete, bool ReturnInspectionComplete, string? Warning);
