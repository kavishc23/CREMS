using CREMS.Api.Services;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] Guid? bookingId = null,
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
        if (bookingId.HasValue) baseQuery = baseQuery.Where(x => x.Id == bookingId);
        IQueryable<Booking> selected = queue switch
        {
            "OnHire" => baseQuery.Where(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt >= todayEnd)),
            "DueToday" => baseQuery.Where(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt >= todayStart && i.EndAt < todayEnd)),
            "Overdue" => baseQuery.Where(x => (x.Status == BookingStatus.ConvertedToRental || (x.Status == BookingStatus.Completed && !x.Inspections.Any(i => i.Type == InspectionType.Return))) && x.Items.Any(i => i.EndAt < todayStart)),
            "ReturnInProgress" => baseQuery.Where(x => x.Status == BookingStatus.ConvertedToRental && x.Inspections.Any(i => i.Type == InspectionType.Return)),
            "AllCompleted" => baseQuery.Where(x => x.Status == BookingStatus.Completed && x.Inspections.Any(i => i.Type == InspectionType.Return)),
            "RecentlyCompleted" => baseQuery.Where(x => x.Status == BookingStatus.Completed && x.Inspections.Any(i => i.Type == InspectionType.Return) && x.UpdatedAt >= recent),
            _ => baseQuery.Where(x => x.Status == BookingStatus.Confirmed && x.Items.Any(i => i.StartAt < todayEnd)),
        };
        if (bookingId.HasValue) selected = baseQuery;
        var counts = new RentalQueueCounts(
            await baseQuery.CountAsync(x => x.Status == BookingStatus.Confirmed && x.Items.Any(i => i.StartAt < todayEnd), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt >= todayEnd), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.ConvertedToRental && x.Items.Any(i => i.EndAt >= todayStart && i.EndAt < todayEnd), cancellationToken),
            await baseQuery.CountAsync(x => (x.Status == BookingStatus.ConvertedToRental || (x.Status == BookingStatus.Completed && !x.Inspections.Any(i => i.Type == InspectionType.Return))) && x.Items.Any(i => i.EndAt < todayStart), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.ConvertedToRental && x.Inspections.Any(i => i.Type == InspectionType.Return), cancellationToken),
            await baseQuery.CountAsync(x => x.Status == BookingStatus.Completed && x.Inspections.Any(i => i.Type == InspectionType.Return) && x.UpdatedAt >= recent, cancellationToken));
        var total = await selected.CountAsync(cancellationToken);
        var rows = await selected.OrderBy(x => x.Items.Select(i => i.StartAt).FirstOrDefault()).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new RentalWorkQueueRow(x.Id, x.BookingNumber, x.Status.ToString(), x.Customer!.Name, x.Customer.Phone,
                x.Branch!.Name, x.Items.Select(i => i.Asset!.AssetNumber).FirstOrDefault(), x.Items.Select(i => i.Asset!.Name).FirstOrDefault(),
                x.Items.Select(i => i.Asset!.Type == AssetType.PropertyUnit ? "PROPERTY_UNIT" : i.Asset.AssetCategory!.Code).FirstOrDefault(), x.Items.Select(i => i.Asset!.Category).FirstOrDefault(),
                x.Charges.Any(c => c.Category == ChargeCategory.Driver || c.Category == ChargeCategory.Operator),
                x.Items.Select(i => (DateTimeOffset?)i.StartAt).FirstOrDefault(), x.Items.Select(i => (DateTimeOffset?)i.EndAt).FirstOrDefault(),
                x.Status != BookingStatus.Confirmed && x.RentalAgreement != null,
                x.Status == BookingStatus.Confirmed || x.RentalAgreement == null ? "Not signed"
                    : x.Status == BookingStatus.Completed && x.Inspections.Any(i => i.Type == InspectionType.Return) ? "Completed"
                    : x.Status == BookingStatus.ConvertedToRental || x.Status == BookingStatus.Completed ? "Active"
                    : "Not signed",
                x.Payments.Where(p => p.Status == PaymentStatus.Recorded && (p.Type == PaymentType.RentalCharge || p.Type == PaymentType.AdditionalCharge)) .Sum(p => (decimal?)p.Amount) ?? 0, x.DepositRequired,
                x.BondStatus.ToString(), x.BondAmountHeld, x.BondDeductionAmount, x.BondRefundAmount,
                x.Inspections.Any(i => i.Type == InspectionType.Handover), x.Inspections.Any(i => i.Type == InspectionType.Return),
                x.Inspections.Where(i => i.Type == InspectionType.Handover).OrderByDescending(i => i.CompletedAt).Select(i => i.ConditionNotes).FirstOrDefault(),
                x.Inspections.Where(i => i.Type == InspectionType.Handover).OrderByDescending(i => i.CompletedAt).Select(i => i.MeterReading).FirstOrDefault(),
                x.Inspections.Where(i => i.Type == InspectionType.Handover).OrderByDescending(i => i.CompletedAt).Select(i => i.FuelLevelPercent).FirstOrDefault(),
                (x.Status == BookingStatus.ConvertedToRental || (x.Status == BookingStatus.Completed && !x.Inspections.Any(i => i.Type == InspectionType.Return))) && x.Items.Any(i => i.EndAt < now) ? "Return is overdue" : x.Customer.IsBlocked ? "Customer account is blocked" : null))
            .ToListAsync(cancellationToken);
        var visibleIds = rows.Select(x => x.Id).ToArray();
        var financialBookings = await db.Bookings.AsNoTracking().Include(x => x.Items).Include(x => x.Charges).Include(x => x.Invoice)
            .Where(x => visibleIds.Contains(x.Id)).AsSplitQuery().ToDictionaryAsync(x => x.Id, cancellationToken);
        var visibleInvoiceIds = financialBookings.Values.Where(x => x.Invoice != null).Select(x => x.Invoice!.Id).ToArray();
        var credits = await db.CreditNotes.AsNoTracking().Where(x => visibleInvoiceIds.Contains(x.InvoiceId) && x.Status == CreditNoteStatus.Applied)
            .GroupBy(x => x.InvoiceId).Select(x => new { Id = x.Key, Subtotal = x.Sum(c => c.Subtotal) }).ToDictionaryAsync(x => x.Id, x => x.Subtotal, cancellationToken);
        rows = rows.Select(row => {
            var booking = financialBookings[row.Id];
            return row with { AmountPaid = row.AmountPaid + (booking.BondSettledAt.HasValue ? booking.BondDeductionAmount : 0), RentalRevenue = booking.Invoice is null ? RentalFinancials.Net(booking) : booking.Invoice.Subtotal - (booking.Invoice.TaxInclusive ? booking.Invoice.TaxAmount : 0) - credits.GetValueOrDefault(booking.Invoice.Id),
                RentalCharge = booking.Invoice?.Total ?? RentalFinancials.Total(booking),
                BalanceDue = Math.Max(0, (booking.Invoice?.Total ?? RentalFinancials.Total(booking)) - row.AmountPaid - (booking.BondSettledAt.HasValue ? booking.BondDeductionAmount : 0)) };
        }).ToList();
        return Ok(new RentalWorkQueueResponse(rows, counts, page, pageSize, total));
    }

    [HttpGet("{bookingId:guid}")]
    public async Task<ActionResult> Get(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().Include(item => item.Items).ThenInclude(item => item.Asset)
            .Include(item => item.Inspections).Include(item => item.Charges).FirstOrDefaultAsync(item => item.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        return Ok(ToResponse(booking));
    }

    [HttpPost("{bookingId:guid}/pre-hire-inspection")]
    public async Task<ActionResult> SavePreHireInspection(Guid bookingId, PreHireInspectionRequest request, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(x => x.Items).ThenInclude(x => x.Asset)
            .Include(x => x.Inspections).FirstOrDefaultAsync(x => x.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        var asset = booking.Items.FirstOrDefault()?.Asset;
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, asset?.DivisionId)) return Forbid();
        if (booking.Status != BookingStatus.Confirmed) return BadRequest(new { message = "Only a confirmed booking can have a pre-hire inspection prepared." });
        if (asset is null || !string.Equals(asset.AssetNumber, request.ScannedAssetNumber?.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Scan or enter the asset allocated to this booking." });
        if (string.IsNullOrWhiteSpace(request.ConditionNotes) || (request.ChecklistItems?.Count ?? 0) + (request.ChecklistFailedItems?.Count ?? 0) == 0 || request.EvidenceDataUrls is null || request.EvidenceDataUrls.Count == 0)
            return BadRequest(new { message = "Complete the checklist, condition notes and at least one inspection photo." });
        if (request.MeterReading < 0 || request.FuelLevelPercent is < 0 or > 100 || request.MeterReading.HasValue && asset.CurrentMeterReading.HasValue && request.MeterReading < asset.CurrentMeterReading)
            return BadRequest(new { message = "Check the meter and fuel readings. The meter cannot decrease and fuel must be between 0 and 100 percent." });
        var configuredError = await ConfiguredHireRules.ValidateInspection(db, booking, InspectionStage.PreHire, request.ChecklistItems, request.ChecklistFailedItems, request.MeterReading, request.FuelLevelPercent, true, cancellationToken);
        if (configuredError is not null) return BadRequest(new { message = configuredError });
        await ConfiguredHireRules.CaptureIfMissing(db, booking, cancellationToken);
        if (request.MeterReading.HasValue) asset.MeterUnit = await ConfiguredHireRules.EffectiveMeterUnit(db, asset, cancellationToken);
        var inspection = RentalInspectionPersistence.GetOrCreatePreparation(db, booking, scope.UserName);
        inspection.MeterReading = request.MeterReading;
        inspection.FuelLevelPercent = request.FuelLevelPercent;
        inspection.ConditionNotes = Normalize(request.ConditionNotes);
        inspection.DamageNotes = Normalize(request.DamageNotes);
        inspection.EvidenceJson = System.Text.Json.JsonSerializer.Serialize(new { checklist = request.ChecklistItems, checklistResults = request.ChecklistAllItems?.Select(label => new { label, passed = request.ChecklistItems?.Contains(label) == true ? (bool?)true : request.ChecklistFailedItems?.Contains(label) == true ? false : null }), photos = request.EvidenceDataUrls, damageZones = request.DamageZones ?? [], accessories = request.AccessoryNotes });
        inspection.CompletedAt = DateTimeOffset.UtcNow;
        inspection.CompletedByUserId = scope.UserId;
        inspection.CompletedByName = scope.UserName;
        if (request.MeterReading.HasValue && asset.CurrentMeterReading != request.MeterReading)
        {
            asset.CurrentMeterReading = request.MeterReading;
            db.AssetMeterReadings.Add(new AssetMeterReading
            {
                AssetId = asset.Id,
                BookingId = booking.Id,
                Type = asset.MeterUnit?.Contains("hour", StringComparison.OrdinalIgnoreCase) == true ? MeterType.EngineHours : MeterType.Odometer,
                Unit = asset.MeterUnit ?? "unit",
                Reading = request.MeterReading.Value,
                FuelPercent = request.FuelLevelPercent,
                Source = MeterReadingSource.PreHireInspection,
                RecordedByUserId = scope.UserId
            });
        }
        AuditWriter.Record(db, scope, "Pre-hire inspection saved", "Booking", booking.Id, $"Pre-hire inspection prepared for {booking.BookingNumber}; asset release remains pending.", booking.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { inspection.Id, message = "Pre-hire inspection saved. Complete agreement signing and checkout at handover." });
    }
    [HttpGet("{bookingId:guid}/inspection-context")]
    public async Task<ActionResult> InspectionContext(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Asset).Include(x => x.Inspections)
            .Include(x => x.Inspections).FirstOrDefaultAsync(x => x.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        var categoryId = booking.Items.FirstOrDefault()?.Asset?.AssetCategoryId;
        var templates = await db.InspectionTemplates.AsNoTracking()
            .Where(x => x.IsActive && x.AssetCategoryId == categoryId && (x.Stage == InspectionStage.PreHire || x.Stage == InspectionStage.PostHire))
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Stage, x.ChecklistJson }).ToListAsync(cancellationToken);
        var preHire = booking.Inspections.Where(x => x.Type == InspectionType.Handover).OrderByDescending(x => x.CompletedAt).FirstOrDefault();
        var configuration = await ConfiguredHireRules.Resolve(db, booking, cancellationToken);
        var categoryUnit = categoryId.HasValue ? await db.AssetCategories.AsNoTracking().Where(x => x.Id == categoryId).Select(x => x.DefaultMeterType).FirstOrDefaultAsync(cancellationToken) : null;
        return Ok(new
        {
            templates = configuration?.Templates ?? templates.Select(t => new InspectionTemplateSnapshot(t.Id, t.Name, t.Stage, t.ChecklistJson)).ToArray(),
            tracksMeter = configuration?.TracksMeter, tracksFuel = configuration?.TracksFuel, meterUnit = categoryUnit ?? configuration?.MeterUnit,
            assetCurrentMeterReading = booking.Items.FirstOrDefault()?.Asset?.CurrentMeterReading,
            preHire = preHire is null ? null : new { preHire.ConditionNotes, preHire.DamageNotes, preHire.EvidenceJson, preHire.MeterReading, preHire.FuelLevelPercent }
        });
    }
    [HttpPost("{bookingId:guid}/handover")]
    public async Task<ActionResult> Handover(Guid bookingId, InspectionRequest request, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Asset).FirstOrDefaultAsync(x => x.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        return Conflict(new { message = "This legacy handover route cannot release an asset. Complete the guided pickup using the rental-agreement pickup endpoint to record the signed agreement, inspection and payments together." });
    }

    [HttpGet("{bookingId:guid}/return-charges")]
    public async Task<ActionResult> ReturnCharges(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Asset).Include(x => x.Inspections).Include(x => x.Charges).Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status != BookingStatus.ConvertedToRental && !(booking.Status == BookingStatus.Completed && !booking.Inspections.Any(x => x.Type == InspectionType.Return))) return BadRequest(new { message = "Only an active rental can be returned." });
        var returnedAt = DateTimeOffset.UtcNow;
        var multiplier = await LateFeeMultiplier(cancellationToken);
        var hireSubtotal = RentalFinancials.Net(booking);
        var taxableSubtotal = Math.Max(0, booking.Items.Where(item => item.HireIsTaxable).Sum(HirePricing.Amount) - booking.DiscountAmount + booking.Charges.Where(charge => charge.IsTaxable).Sum(charge => RentalFinancials.Money(charge.Quantity * charge.UnitRate)) + RentalFinancials.Unitemised(booking));
        var paidHire = booking.Payments.Where(payment => payment.Status == PaymentStatus.Recorded && (payment.Type == PaymentType.RentalCharge || payment.Type == PaymentType.AdditionalCharge)).Sum(payment => payment.Amount);
        return Ok(new { returnedAt, lateFee = ReturnChargePolicy.LateFee(booking.Items, returnedAt, multiplier), multiplier, hireSubtotal, taxableSubtotal, taxRate = booking.TaxRate, paidHire });
    }

    [HttpPost("{bookingId:guid}/return")]
    public async Task<ActionResult> Return(Guid bookingId, ReturnInspectionRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await MaintenanceRules.BeginAsync(db, cancellationToken);
        var booking = await db.Bookings.Include(item => item.Customer).Include(item => item.Items).ThenInclude(item => item.Asset)
            .Include(item => item.Inspections).Include(item => item.Payments).Include(item => item.Charges)
            .Include(item => item.Invoice).ThenInclude(invoice => invoice!.Lines)
            .FirstOrDefaultAsync(item => item.Id == bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status != BookingStatus.ConvertedToRental && !(booking.Status == BookingStatus.Completed && !booking.Inspections.Any(x => x.Type == InspectionType.Return)))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["Only an active rental can be returned."] }));
        if (booking.Invoice?.Status == InvoiceStatus.Voided) return Conflict(new { message = "Resolve the voided invoice in Finance before completing this return." });
        var allocatedAssetNumber = booking.Items.FirstOrDefault()?.Asset?.AssetNumber;
        if (string.IsNullOrWhiteSpace(request.ScannedAssetNumber) ||
            !string.Equals(request.ScannedAssetNumber.Trim(), allocatedAssetNumber, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["asset"] = ["Scan or enter the asset QR number linked to this rental."] }));
        if (new[] { request.AdditionalCharges, request.LateFee ?? 0, request.ExcessUsageCharge, request.RefuellingCharge, request.CleaningCharge, request.DamageCharge, request.BondDeductionAmount }.Any(x => x != RentalFinancials.Money(x)))
            return BadRequest(new { message = "Enter charges and bond amounts with no more than two decimal places." });
        if (request.BondDeductionAmount < 0 || request.BondDeductionAmount > booking.BondAmountHeld)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["bond"] = ["The bond deduction must be between zero and the amount held."] }));
        if (request.BondDeductionAmount > 0 && string.IsNullOrWhiteSpace(request.BondDeductionReason))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["bondReason"] = ["Give a clear reason for every bond deduction."] }));
        if ((request.ChecklistItems?.Count ?? 0) + (request.ChecklistFailedItems?.Count ?? 0) == 0 || string.IsNullOrWhiteSpace(request.ConditionNotes))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["inspection"] = ["Complete the category checklist and record the post-hire condition."] }));
        if (request.ChecklistAllItems is { Count: > 0 } && request.ChecklistAllItems.Any(label => request.ChecklistItems?.Contains(label) != true && request.ChecklistFailedItems?.Contains(label) != true))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["checklist"] = ["Mark every checklist item as passed or failed."] }));
        if (request.ChecklistFailedItems?.Count > 0 && string.IsNullOrWhiteSpace(request.DamageNotes))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["damageNotes"] = ["Describe every failed checklist item before completing the return."] }));
        if (request.EvidenceDataUrls is null || request.EvidenceDataUrls.Count == 0)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["photos"] = ["Attach at least one return inspection photograph."] }));

        if (string.IsNullOrWhiteSpace(request.SignatureName) || string.IsNullOrWhiteSpace(request.SignatureDataUrl))
            return BadRequest(new { message = "Record the customer acknowledgement name and drawn signature before completing the return." });
        if (request.MeterReading < 0 || request.FuelLevelPercent is < 0 or > 100)
            return BadRequest(new { message = "Meter readings cannot be negative and fuel must be between 0 and 100 percent." });
        var lastReading = booking.Inspections.Where(x => x.Type == InspectionType.Handover).Select(x => x.MeterReading)
            .Concat(booking.Items.Where(x => x.Asset != null).Select(x => x.Asset!.CurrentMeterReading)).Max();
        if (request.MeterReading.HasValue && lastReading.HasValue && request.MeterReading < lastReading)
            return BadRequest(new { message = "The return meter reading cannot be lower than the recorded starting reading." });

        var returnedAt = request.ReturnedAt ?? DateTimeOffset.UtcNow;
        if (returnedAt > DateTimeOffset.UtcNow || booking.Items.Any(x => returnedAt < x.StartAt))
            return BadRequest(new { message = "The return time must be after hire starts and no later than now." });
        var calculatedLateFee = ReturnChargePolicy.LateFee(booking.Items, returnedAt, await LateFeeMultiplier(cancellationToken));
        var lateFee = request.LateFee ?? calculatedLateFee;
        var configuredError = await ConfiguredHireRules.ValidateInspection(db, booking, InspectionStage.PostHire, request.ChecklistItems, request.ChecklistFailedItems, request.MeterReading, request.FuelLevelPercent, false, cancellationToken);
        if (configuredError is not null) return BadRequest(new { message = configuredError });
        var returnInspection = CreateInspection(booking.Id, InspectionType.Return, request, scope);
        returnInspection.CompletedAt = returnedAt;
        db.RentalInspections.Add(returnInspection);
        var returnChargeTotal = RentalFinancials.Money(request.AdditionalCharges + lateFee + request.ExcessUsageCharge + request.RefuellingCharge + request.CleaningCharge + request.DamageCharge);
        // Preserve legacy agreed extras, then persist each return surcharge for future documents and reports.
        var legacyExtras = RentalFinancials.Unitemised(booking);
        if (legacyExtras > 0) booking.Charges.Add(new BookingCharge { BookingId = booking.Id, Description = booking.AdditionalChargesDescription ?? "Other agreed charges", Category = ChargeCategory.Other, Unit = ChargeUnit.Fixed, Quantity = 1, UnitRate = legacyExtras, IsTaxable = true });
        foreach (var (description, amount, category) in new[] {
            ("Late return", lateFee, ChargeCategory.Other), ("Excess kilometres / hours", request.ExcessUsageCharge, ChargeCategory.Other),
            ("Refuelling", request.RefuellingCharge, ChargeCategory.Fuel), ("Cleaning", request.CleaningCharge, ChargeCategory.Cleaning),
            ("Damage", request.DamageCharge, ChargeCategory.Other), (request.AdditionalChargesDescription ?? "Other return charges", request.AdditionalCharges, ChargeCategory.Other)
        }.Where(x => x.Item2 > 0))
            booking.Charges.Add(new BookingCharge { BookingId = booking.Id, Description = description, Category = category, Unit = ChargeUnit.Fixed, Quantity = 1, UnitRate = RentalFinancials.Money(amount), IsTaxable = true });
        // Explicitly insert new charges: their application-generated keys must not be
        // mistaken for existing rows when EF discovers them through the collection.
        foreach (var charge in booking.Charges.Where(x => db.Entry(x).State == EntityState.Detached))
            db.BookingCharges.Add(charge);
        booking.AdditionalCharges = RentalFinancials.Services(booking);
        var returnChargeDescription = Normalize(request.AdditionalChargesDescription);
        booking.AdditionalChargesDescription = string.Join(", ", booking.Charges.Select(x => x.Description)
            .Concat(returnChargeDescription is null ? [] : [returnChargeDescription]).Where(x => !string.IsNullOrWhiteSpace(x)));
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
            db.RentalPayments.Add(new RentalPayment { BookingId = booking.Id, Type = PaymentType.BondRefund,
                Method = request.BondRefundMethod, Amount = booking.BondRefundAmount,
                ReceiptNumber = $"BRF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                Note = booking.BondDeductionAmount > 0 ? $"Bond refund after deduction: {booking.BondDeductionReason}" : "Full refundable bond returned.",
                RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        var hasDamage = !string.IsNullOrWhiteSpace(request.DamageNotes) || request.ChecklistFailedItems?.Count > 0;
        foreach (var item in booking.Items.Where(item => item.Asset is not null))
        {
            var asset=item.Asset!;if(request.MeterReading.HasValue)asset.MeterUnit=await ConfiguredHireRules.EffectiveMeterUnit(db,asset,cancellationToken);var from=asset.Status;
            var hasUnreleasedMaintenance = await db.MaintenanceJobs.AnyAsync(x => x.AssetId == asset.Id &&
                (x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled || x.ReleasedAt == null), cancellationToken);
            asset.Status=hasDamage || hasUnreleasedMaintenance ? AssetStatus.Maintenance : AssetStatus.Available;
            db.AssetLifecycleEvents.Add(new AssetLifecycleEvent{AssetId=asset.Id,BookingId=booking.Id,Type=hasDamage?AssetLifecycleEventType.DamageReported:AssetLifecycleEventType.PostHireInspection,FromStatus=from,ToStatus=asset.Status,MeterReading=request.MeterReading,Notes=Normalize(request.DamageNotes)??$"Returned on {booking.BookingNumber}",RecordedByUserId=scope.UserId,RecordedByName=scope.UserName});
            if(request.MeterReading.HasValue){asset.CurrentMeterReading=request.MeterReading;db.AssetMeterReadings.Add(new AssetMeterReading{AssetId=asset.Id,BookingId=booking.Id,Type=asset.MeterUnit?.Contains("hour",StringComparison.OrdinalIgnoreCase)==true?MeterType.EngineHours:MeterType.Odometer,Unit=asset.MeterUnit??"unit",Reading=request.MeterReading.Value,FuelPercent=request.FuelLevelPercent,Source=MeterReadingSource.PostHireInspection,RecordedByUserId=scope.UserId});}
            // Preserve a separate traceable job for each newly reported return fault.
            if (hasDamage)
                db.MaintenanceJobs.Add(new MaintenanceJob { JobNumber = $"MNT-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}", AssetId = asset.Id, BranchId = asset.BranchId, SourceType = "Damage", SourceReference = booking.BookingNumber, ReportedByName = scope.UserName, ServiceType = "Post-hire damage assessment", FaultDescription = Normalize(request.DamageNotes) ?? "Failed post-hire checklist items require assessment.", Description = $"Automatically referred from return of {booking.BookingNumber}.", Priority = MaintenancePriority.High, Status = MaintenanceStatus.Open, MeterReading = request.MeterReading });
        }
        var invoiceSubtotal = RentalFinancials.Net(booking);
        var invoiceTax = HirePricing.Tax(booking);
        var paid = await db.RentalPayments.Where(x => x.BookingId == bookingId && x.Status == PaymentStatus.Recorded &&
            (x.Type == PaymentType.RentalCharge || x.Type == PaymentType.AdditionalCharge)).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;
        if (request.BondDeductionAmount > Math.Max(0, invoiceSubtotal + invoiceTax - paid))
            return BadRequest(new { message = "The bond applied cannot exceed the unpaid final invoice. Refund the remaining bond." });
        paid += request.BondDeductionAmount;
        if (request.AmountCollected != decimal.Round(request.AmountCollected, 2)) return BadRequest(new { message = "Enter the payment amount in dollars and cents, with no more than two decimal places." });
        if (request.AmountCollected > Math.Max(0, invoiceSubtotal + invoiceTax - paid)) return BadRequest(new { message = "The payment exceeds the outstanding final invoice balance." });
        if (request.AmountCollected > 0)
        {
            if (string.IsNullOrWhiteSpace(request.ReceiptNumber)) return BadRequest(new { message = "Enter a receipt or transaction reference for the return payment." });
            if (!Enum.IsDefined(request.PaymentMethod) || request.PaymentMethod == PaymentMethod.PurchaseOrder) return BadRequest(new { message = "Select the actual payment method used to receive money." });
            var receipt = request.ReceiptNumber.Trim();
            if (receipt.Length > 60) return BadRequest(new { message = "Keep the payment reference to 60 characters or fewer." });
            if (await db.RentalPayments.AnyAsync(payment => payment.ReceiptNumber == receipt, cancellationToken)) return Conflict(new { message = "That payment reference is already recorded. Review existing receipts before recording another payment." });
            db.RentalPayments.Add(new RentalPayment { BookingId = booking.Id, Type = PaymentType.RentalCharge, Method = request.PaymentMethod, Amount = request.AmountCollected, ReceiptNumber = receipt, Note = "Final hire settlement received at return.", RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
            paid += request.AmountCollected;
        }
        var invoiceLines = booking.Items.Select(item => new InvoiceLine { Description = $"{item.Asset?.Name ?? "Rental"} hire ({item.HireUnit})", Quantity = HirePricing.Quantity(item), UnitPrice = item.DailyRate, TaxRate = booking.TaxRate, IsTaxable = item.HireIsTaxable }).ToList();
        if (booking.DiscountAmount > 0)
            invoiceLines.Add(new InvoiceLine { Description = "Discount", Quantity = 1, UnitPrice = -booking.DiscountAmount, TaxRate = booking.TaxRate, IsTaxable = true });
        invoiceLines.AddRange(booking.Charges.Select(x => new InvoiceLine { Description = x.Description, Quantity = x.Quantity,
            UnitPrice = x.UnitRate, TaxRate = booking.TaxRate, IsTaxable = x.IsTaxable }));

        var invoice = booking.Invoice ?? new RentalInvoice { LineItemsJson = "[]", BookingId = booking.Id, InvoiceNumber = $"INV-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}" };
        if (booking.Invoice is not null) db.InvoiceLines.RemoveRange(invoice.Lines);
        invoice.LineItemsJson = System.Text.Json.JsonSerializer.Serialize(invoiceLines.Select(line => new { description = line.Description, quantity = line.Quantity, unitPrice = line.UnitPrice, amount = RentalFinancials.Money(line.Quantity * line.UnitPrice), isTaxable = line.IsTaxable, taxRate = line.TaxRate }));
        invoice.Subtotal = invoiceSubtotal; invoice.TaxAmount = invoiceTax; invoice.Total = invoiceSubtotal + invoiceTax;
        invoice.AmountPaid = paid; invoice.BalanceDue = Math.Max(0, invoice.Total - paid);
        invoice.Status = paid >= invoice.Total ? InvoiceStatus.Paid : paid > 0 ? InvoiceStatus.PartiallyPaid : InvoiceStatus.Issued;
        invoice.Lines = invoiceLines;
        if (booking.Invoice is null) db.RentalInvoices.Add(invoice);
        if (!string.IsNullOrWhiteSpace(booking.Customer?.Email)) db.RentalNotifications.Add(new RentalNotification { BookingId = booking.Id, Channel = NotificationChannel.Email, Recipient = booking.Customer.Email, Subject = $"Final invoice {invoice.InvoiceNumber}", Message = $"Your rental {booking.BookingNumber} has been returned. Final total: FJD {invoice.Total:0.00}; balance due: FJD {invoice.BalanceDue:0.00}." });
        var agreement = await db.RentalAgreements.FirstOrDefaultAsync(x => x.BookingId == bookingId, cancellationToken);
        if (agreement is not null) agreement.Status = AgreementStatus.Completed;
        AuditWriter.Record(db, scope, "Rental returned", "Booking", booking.Id,
            $"{booking.BookingNumber} was returned{(hasDamage ? " with damage requiring inspection" : string.Empty)}.", booking.BranchId,
            null, $"Returned at: {returnedAt:O}; calculated late fee: {calculatedLateFee:0.00}; applied late fee: {lateFee:0.00}; additional charges: {returnChargeTotal:0.00}");
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(booking));
    }

    private async Task<decimal> LateFeeMultiplier(CancellationToken token)
    {
        var value = await db.SystemSettings.AsNoTracking().Where(x => x.Key == "rentals.lateFeeMultiplier").Select(x => x.Value).FirstOrDefaultAsync(token);
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var multiplier) && multiplier is >= 0 and <= 10 ? multiplier : 1m;
    }

    private static RentalInspection CreateInspection(Guid bookingId, InspectionType type,
        InspectionRequest request, StaffDataScope scope) => new()
    {
        BookingId = bookingId, Type = type, IdentificationVerified = request.IdentificationVerified,
        DriverLicenceVerified = request.DriverLicenceVerified, MeterReading = request.MeterReading,
        FuelLevelPercent = request.FuelLevelPercent, ConditionNotes = Normalize(request.ConditionNotes),
        DamageNotes = Normalize(request.DamageNotes), SignatureName = Normalize(request.SignatureName),
        SignatureDataUrl = request.SignatureDataUrl, PaymentVerified = request.PaymentVerified,
        EvidenceJson = System.Text.Json.JsonSerializer.Serialize(new { checklist = request.ChecklistItems ?? [], checklistResults = request.ChecklistAllItems?.Select(label => new { label, passed = request.ChecklistItems?.Contains(label) == true ? (bool?)true : request.ChecklistFailedItems?.Contains(label) == true ? false : null }), photos = request.EvidenceDataUrls ?? [], damageZones = request.DamageZones ?? [], accessories = request.AccessoryNotes }),
        CompletedByUserId = scope.UserId, CompletedByName = scope.UserName,
    };

    private static object ToResponse(Booking booking)
    {
        var subtotal = booking.Items.Sum(item => HirePricing.Amount(item));
        var taxable = Math.Max(0, subtotal - booking.DiscountAmount + booking.AdditionalCharges);
        return new { booking.Id, booking.BookingNumber, booking.Status, booking.DepositRequired,
            bond = new { required = booking.DepositRequired, held = booking.BondAmountHeld, deduction = booking.BondDeductionAmount, booking.BondDeductionReason, refund = booking.BondRefundAmount, status = booking.BondStatus.ToString(), booking.BondSettledAt },
            booking.DiscountAmount, booking.TaxRate, booking.AdditionalCharges, booking.AdditionalChargesDescription,
            Subtotal = subtotal, TaxAmount = HirePricing.Tax(booking), Total = RentalFinancials.Total(booking),
            Inspections = booking.Inspections.OrderBy(item => item.CompletedAt).Select(item => new { item.Id, item.Type,
                item.IdentificationVerified, item.DriverLicenceVerified, item.MeterReading, item.FuelLevelPercent,
                item.ConditionNotes, item.DamageNotes, item.EvidenceJson, item.SignatureName, item.SignatureDataUrl, item.CompletedByName, item.CompletedAt }) };
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
    public string? AccessoryNotes { get; init; }
    public bool PaymentVerified { get; init; }
    public IReadOnlyList<string>? EvidenceDataUrls { get; init; }
    public IReadOnlyList<string>? DamageZones { get; init; }
    public IReadOnlyList<string>? ChecklistItems { get; init; }
    public IReadOnlyList<string>? ChecklistAllItems { get; init; }
    public IReadOnlyList<string>? ChecklistFailedItems { get; init; }
}
public sealed record ReturnInspectionRequest(
    string ScannedAssetNumber,
    bool IdentificationVerified,
    bool DriverLicenceVerified,
    decimal? MeterReading,
    int? FuelLevelPercent,
    string? ConditionNotes,
    string? DamageNotes,
    string? SignatureName,
    [Range(0, 1000000)] decimal AdditionalCharges,
    string? AdditionalChargesDescription,
    [Range(0, 1000000)] decimal? LateFee,
    [Range(0, 1000000)] decimal ExcessUsageCharge,
    [Range(0, 1000000)] decimal RefuellingCharge,
    [Range(0, 1000000)] decimal CleaningCharge,
    [Range(0, 1000000)] decimal DamageCharge,
    [Range(0, 1000000)] decimal BondDeductionAmount,
    string? BondDeductionReason,
    PaymentMethod BondRefundMethod) : InspectionRequest(
        IdentificationVerified, DriverLicenceVerified, MeterReading, FuelLevelPercent,
        ConditionNotes, DamageNotes, SignatureName)
{
    public DateTimeOffset? ReturnedAt { get; init; }
    [Range(0, 1000000)] public decimal AmountCollected { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public string? ReceiptNumber { get; init; }
}
public sealed record RentalQueueCounts(int PickupToday, int OnHire, int DueToday, int Overdue, int ReturnInProgress, int RecentlyCompleted);
public sealed record RentalWorkQueueResponse(IReadOnlyList<RentalWorkQueueRow> Items, RentalQueueCounts Counts, int Page, int PageSize, int Total);
public sealed record RentalWorkQueueRow(Guid Id, string BookingNumber, string Status, string CustomerName, string? CustomerPhone,
    string BranchName, string? AssetNumber, string? AssetName, string? AssetCategoryCode, string? AssetCategory,
    bool HasProfessionalPersonnel, DateTimeOffset? StartAt, DateTimeOffset? EndAt,
    bool HasAgreement, string AgreementStatus, decimal AmountPaid, decimal DepositRequired,
    string BondStatus, decimal BondAmountHeld, decimal BondDeductionAmount, decimal BondRefundAmount,
    bool PreHireInspectionComplete, bool ReturnInspectionComplete, string? PreHireCondition,
    decimal? PreHireMeterReading, int? PreHireFuelLevelPercent, string? Warning)
{
    public decimal RentalRevenue { get; init; }
    public decimal RentalCharge { get; init; }
    public decimal BalanceDue { get; init; }
}

public sealed record PreHireInspectionRequest(string? ScannedAssetNumber, decimal? MeterReading, int? FuelLevelPercent,
    string? ConditionNotes, string? DamageNotes, IReadOnlyList<string>? ChecklistItems,
    IReadOnlyList<string>? EvidenceDataUrls, IReadOnlyList<string>? DamageZones, string? AccessoryNotes, IReadOnlyList<string>? ChecklistAllItems = null, IReadOnlyList<string>? ChecklistFailedItems = null);
