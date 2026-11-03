using System.Text.Json;
using System.Text.Encodings.Web;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/rental-agreements")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class RentalAgreementsController(ApplicationDbContext db, CurrentStaffScope staffScope, IEmailQueue emailQueue) : ControllerBase
{
    private const string TermsVersion = "CREMS-RA-2026.1-DRAFT";
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    [HttpGet("{bookingId:guid}")]
    public async Task<ActionResult> Get(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await LoadBooking(bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        // A confirmed booking is still awaiting handover, even if legacy data has
        // an agreement row attached. Never present it as signed before pickup.
        if (booking.Status == BookingStatus.Confirmed) return Ok(BuildDraft(booking));
        var agreement = await db.RentalAgreements.AsNoTracking().FirstOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        return Ok(agreement is null ? BuildDraft(booking) : BuildApproved(agreement, booking));
    }

    [HttpGet("{bookingId:guid}/pickup-readiness")]
    public async Task<ActionResult> GetPickupReadiness(Guid bookingId, CancellationToken token)
    {
        var booking = await LoadBooking(bookingId, token);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        var reasons = await PickupReadiness.Check(db, booking, token);
        return Ok(new { canPickup = reasons.Count == 0, reasons });
    }

    [HttpPost("{bookingId:guid}/pickup")]
    public async Task<ActionResult> Pickup(Guid bookingId, PickupRentalRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await MaintenanceRules.BeginAsync(db, cancellationToken);
        var booking = await LoadBooking(bookingId, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status != BookingStatus.Confirmed)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["The booking must be confirmed before its rental agreement is signed."] }));
        var allocatedAssetNumber = booking.Items.FirstOrDefault()?.Asset?.AssetNumber;
        if (string.IsNullOrWhiteSpace(request.ScannedAssetNumber) ||
            !string.Equals(request.ScannedAssetNumber.Trim(), allocatedAssetNumber, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["asset"] = ["Scan or enter the allocated asset QR number before handover."] }));
        var readinessProblems = await PickupReadiness.Check(db, booking, cancellationToken);
        if (readinessProblems.Count > 0) return Conflict(new { message = string.Join(" ", readinessProblems) });
        var customerWillDrive = booking.Items.Any(x => x.Asset is not null && AssetCategoryPolicy.IsVehicle(x.Asset.Type)) &&
            !booking.Charges.Any(x => x.Category is ChargeCategory.Driver or ChargeCategory.Operator);
        if (!request.IdentificationVerified || customerWillDrive && !request.DriverLicenceVerified || !request.PaymentVerified)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["verification"] = [customerWillDrive ? "Identification, driver licence and payment verification are required before pickup." : "Customer identification and payment verification are required before pickup."] }));
        if (!request.CustomerAcceptedTerms || !request.AgentApproved || string.IsNullOrWhiteSpace(request.CustomerSignatureName) || string.IsNullOrWhiteSpace(request.CustomerSignatureDataUrl))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["signature"] = ["Customer acceptance, a drawn customer signature and agent approval are required."] }));
        if (request.ChecklistItems is null || request.ChecklistItems.Count == 0 || string.IsNullOrWhiteSpace(request.ConditionNotes))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["inspection"] = ["Complete the category checklist and record the pre-hire condition."] }));
        if (request.ChecklistFailedItems?.Count > 0 || request.ChecklistAllItems is { Count: > 0 } && request.ChecklistAllItems.Any(label => !request.ChecklistItems.Contains(label)))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["checklist"] = ["Resolve every failed or pending pre-hire check before pickup."] }));
        if (request.EvidenceDataUrls is null || request.EvidenceDataUrls.Count == 0)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["photos"] = ["Attach at least one pre-hire inspection photograph."] }));
        var configuredError = await ConfiguredHireRules.ValidateInspection(db, booking, InspectionStage.PreHire, request.ChecklistItems, request.ChecklistFailedItems, request.MeterReading, request.FuelLevelPercent, false, cancellationToken);
        if (configuredError is not null) return BadRequest(new { message = configuredError });
        await ConfiguredHireRules.CaptureIfMissing(db, booking, cancellationToken);
        if (customerWillDrive && (string.IsNullOrWhiteSpace(request.LicenceNumber) || !DriverLicenceRules.ValidNumber(request.LicenceNumber) || !DriverLicenceRules.ValidClasses(request.LicenceClass) || !request.LicenceExpiry.HasValue || request.LicenceExpiry <= DateOnly.FromDateTime(DateTime.UtcNow)))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["licence"] = ["Enter a numeric licence number, select every applicable class from 1 to 8, and enter a future expiry date."] }));
        // Older confirmed bookings may already contain an agreement from the
        // previous flow. Reuse that record at checkout with the fresh signature
        // instead of blocking handover or creating a duplicate agreement.
        var existingAgreement = await db.RentalAgreements.FirstOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        if (await db.AssetLifecycleEvents.AnyAsync(item => item.BookingId == bookingId && item.Type == AssetLifecycleEventType.CheckedOut, cancellationToken))
            return Conflict(new { message = "This asset has already been checked out. Refresh the rental queue before trying again." });

        if (request.MeterReading.HasValue && booking.Items.Any(x => x.Asset != null && x.Asset.CurrentMeterReading.HasValue && request.MeterReading < x.Asset.CurrentMeterReading))
            return BadRequest(new { message = "The pickup meter reading cannot be lower than the asset's recorded reading." });
        var draft = BuildAgreementData(booking);
        if (request.AmountCollected > Math.Max(0, booking.DepositRequired - booking.BondAmountHeld))
            return BadRequest(new { message = "The amount collected exceeds the remaining refundable bond. Previously recorded bond payments are already included." });
        if (booking.BondAmountHeld + request.AmountCollected < booking.DepositRequired)
            return BadRequest(new { message = "Receive the full refundable bond before releasing the asset." });
        var paidHire = await db.RentalPayments.Where(payment => payment.BookingId == bookingId && payment.Status == PaymentStatus.Recorded && (payment.Type == PaymentType.RentalCharge || payment.Type == PaymentType.AdditionalCharge)).SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0;
        var dueHire = Math.Max(0, decimal.Round(draft.Pricing.Total - paidHire, 2, MidpointRounding.AwayFromZero));
        if (request.AmountCollected != decimal.Round(request.AmountCollected, 2) || request.RentalAmountCollected != decimal.Round(request.RentalAmountCollected, 2)) return BadRequest(new { message = "Enter hire and bond amounts with no more than two decimal places." });
        if (request.RentalAmountCollected < 0 || request.RentalAmountCollected > dueHire) return BadRequest(new { message = "The hire payment must be between zero and the outstanding hire balance." });
        if ((request.AmountCollected > 0 || request.RentalAmountCollected > 0) && string.IsNullOrWhiteSpace(request.ReceiptNumber)) return BadRequest(new { message = "Enter the receipt or transaction reference for the money received." });
        if (request.RentalAmountCollected < dueHire && (!request.PayHireOnReturn || string.IsNullOrWhiteSpace(request.PaymentDeferralReason))) return BadRequest(new { message = "Collect the hire balance or provide a supervisor-approved reason to pay at return." });
        if (request.RentalAmountCollected < dueHire && !scope.IsAdministrator && !User.IsInRole(SystemRoles.BranchManager)) return BadRequest(new { message = "A branch manager or administrator must approve hire payment at return." });
        if ((request.AmountCollected > 0 || request.RentalAmountCollected > 0) && (!Enum.IsDefined(request.PaymentMethod) || request.PaymentMethod == PaymentMethod.PurchaseOrder)) return BadRequest(new { message = "Select the actual payment method used to receive money." });
        var paymentReference = request.ReceiptNumber?.Trim();
        if (paymentReference?.Length > 58) return BadRequest(new { message = "Keep the receipt or transaction reference to 58 characters or fewer." });
        var splitReceipt = request.RentalAmountCollected > 0 && request.AmountCollected > 0;
        var hireReceipt = splitReceipt ? $"{paymentReference}-H" : paymentReference;
        var bondReceipt = splitReceipt ? $"{paymentReference}-B" : paymentReference;
        if ((request.RentalAmountCollected > 0 && await db.RentalPayments.AnyAsync(payment => payment.ReceiptNumber == hireReceipt, cancellationToken)) || (request.AmountCollected > 0 && await db.RentalPayments.AnyAsync(payment => payment.ReceiptNumber == bondReceipt, cancellationToken))) return Conflict(new { message = "That receipt is already recorded. Review existing payments before recording it again." });
        var now = DateTimeOffset.UtcNow;
        if (request.RentalAmountCollected > 0) db.RentalPayments.Add(new RentalPayment { BookingId = booking.Id, Type = PaymentType.RentalCharge, Method = request.PaymentMethod, Amount = request.RentalAmountCollected, ReceiptNumber = hireReceipt!, Note = $"Hire payment collected at pickup. Transaction reference: {paymentReference}", RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        if (request.RentalAmountCollected < dueHire) AuditWriter.Record(db, scope, "Hire payment deferred to return", "Booking", booking.Id, request.PaymentDeferralReason!.Trim(), booking.BranchId);
        var agreement = existingAgreement ?? new RentalAgreement
        {
            AgreementNumber = await RentalSettings.Reference(db, true, cancellationToken),
            BookingId = booking.Id,
            TermsVersion = TermsVersion,
            TermsJson = JsonSerializer.Serialize(draft.Terms, SnapshotJson),
            CustomerSnapshotJson = JsonSerializer.Serialize(draft.Customer, SnapshotJson),
            AssetSnapshotJson = JsonSerializer.Serialize(draft.Asset, SnapshotJson),
            PricingSnapshotJson = JsonSerializer.Serialize(draft.Pricing, SnapshotJson),
            CustomerSignatureName = request.CustomerSignatureName.Trim(),
            ApprovedByName = scope.UserName,
        };
        agreement.BranchId = booking.BranchId;
        agreement.TermsVersion = TermsVersion;
        agreement.TermsJson = JsonSerializer.Serialize(draft.Terms, SnapshotJson);
        agreement.CustomerSnapshotJson = JsonSerializer.Serialize(draft.Customer, SnapshotJson);
        agreement.AssetSnapshotJson = JsonSerializer.Serialize(draft.Asset, SnapshotJson);
        agreement.PricingSnapshotJson = JsonSerializer.Serialize(draft.Pricing, SnapshotJson);
        agreement.CustomerSignatureName = request.CustomerSignatureName.Trim();
        agreement.CustomerSignatureDataUrl = request.CustomerSignatureDataUrl;
        agreement.CustomerSignedAt = now;
        agreement.Status = AgreementStatus.Active;
        agreement.ApprovedByUserId = scope.UserId;
        agreement.ApprovedByName = scope.UserName;
        agreement.ApprovedAt = now;
        if (existingAgreement is null) db.RentalAgreements.Add(agreement);
        booking.RentalAgreement = agreement;
        // A saved pre-hire inspection occupies the unique handover slot. Finalize
        // it in place rather than creating a second record.
        var existingInspection = booking.Inspections.FirstOrDefault(x => x.Type == InspectionType.Handover);
        var inspection = existingInspection ?? new RentalInspection { BookingId = booking.Id, Type = InspectionType.Handover };
        if (existingInspection is null) db.RentalInspections.Add(inspection);
        inspection.IdentificationVerified = request.IdentificationVerified;
        inspection.DriverLicenceVerified = request.DriverLicenceVerified;
        inspection.MeterReading = request.MeterReading;
        inspection.FuelLevelPercent = request.FuelLevelPercent;
        inspection.ConditionNotes = Normalize(request.ConditionNotes);
        inspection.DamageNotes = Normalize(request.DamageNotes);
        inspection.SignatureName = request.CustomerSignatureName.Trim();
        inspection.SignatureDataUrl = request.CustomerSignatureDataUrl;
        inspection.PaymentVerified = request.PaymentVerified;
        inspection.EvidenceJson = JsonSerializer.Serialize(new { checklist = request.ChecklistItems ?? [], checklistResults = request.ChecklistAllItems?.Select(label => new { label, passed = request.ChecklistItems?.Contains(label) == true ? (bool?)true : request.ChecklistFailedItems?.Contains(label) == true ? false : null }), photos = request.EvidenceDataUrls ?? [], damageZones = request.DamageZones ?? [], accessories = request.AccessoryNotes }, SnapshotJson);
        inspection.CompletedByUserId = scope.UserId;
        inspection.CompletedByName = scope.UserName;
        inspection.CompletedAt = now;
        if (customerWillDrive)
        {
            var driver = await db.AuthorizedDrivers.FirstOrDefaultAsync(existing => existing.BookingId == booking.Id && existing.LicenceNumber == request.LicenceNumber!.Trim(), cancellationToken);
            if (driver is null)
            {
                driver = new AuthorizedDriver { BookingId = booking.Id, FullName = request.CustomerSignatureName.Trim(), LicenceNumber = request.LicenceNumber!.Trim() };
                db.AuthorizedDrivers.Add(driver);
            }
            driver.FullName = request.CustomerSignatureName.Trim();
            driver.LicenceClass = DriverLicenceRules.NormalizeClasses(request.LicenceClass!);
            driver.LicenceExpiry = request.LicenceExpiry!.Value; driver.IsPrimary = true; driver.Verified = true;
        }
        if (request.AmountCollected > 0)
        {
            db.RentalPayments.Add(new RentalPayment
            {
                BookingId = booking.Id, Type = PaymentType.BondCollection, Method = request.PaymentMethod,
                Amount = request.AmountCollected, ReceiptNumber = bondReceipt!, Note = $"Refundable bond collected at pickup. Transaction reference: {paymentReference}",
                RecordedByUserId = scope.UserId, RecordedByName = scope.UserName,
            });
            booking.BondAmountHeld += request.AmountCollected;
            booking.BondStatus = booking.BondAmountHeld >= booking.DepositRequired ? BondStatus.Held : BondStatus.AwaitingPayment;
        }
        if (!string.IsNullOrWhiteSpace(booking.Customer?.Email)) QueueAgreementEmail(agreement, booking);
        booking.Status = BookingStatus.ConvertedToRental;
        booking.UpdatedAt = now;
        foreach (var item in booking.Items.Where(item => item.Asset is not null))
        {
            var asset = item.Asset!; var fromStatus = asset.Status; asset.Status = CREMS.Api.Domain.Assets.AssetStatus.Rented;
            db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, BookingId = booking.Id, Type = AssetLifecycleEventType.CheckedOut, FromStatus = fromStatus, ToStatus = asset.Status, MeterReading = request.MeterReading, Notes = $"Checked out on {agreement.AgreementNumber}", RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
            if (request.MeterReading.HasValue) { asset.MeterUnit = await ConfiguredHireRules.EffectiveMeterUnit(db, asset, cancellationToken); asset.CurrentMeterReading = request.MeterReading; }
            if (request.MeterReading.HasValue) db.AssetMeterReadings.Add(new AssetMeterReading { AssetId = asset.Id, BookingId = booking.Id, Type = asset.MeterUnit?.Contains("hour", StringComparison.OrdinalIgnoreCase) == true ? MeterType.EngineHours : MeterType.Odometer, Unit = asset.MeterUnit ?? "unit", Reading = request.MeterReading.Value, FuelPercent = request.FuelLevelPercent, Source = MeterReadingSource.PreHireInspection, RecordedByUserId = scope.UserId });
        }
        AuditWriter.Record(db, scope, "Rental pickup completed", "Booking", booking.Id,
            $"{agreement.AgreementNumber} was signed and {booking.BookingNumber} was handed over to the customer.", booking.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Ok(BuildApproved(agreement, booking));
    }

    [HttpPost("{bookingId:guid}/send")]
    public async Task<ActionResult> Send(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await LoadBooking(bookingId, cancellationToken); if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(booking.BranchId, booking.Items.FirstOrDefault()?.Asset?.DivisionId)) return Forbid();
        if (booking.Status is not (BookingStatus.ConvertedToRental or BookingStatus.Completed))
            return BadRequest(new { message = "Complete pickup before sending a signed rental agreement." });
        var agreement = await db.RentalAgreements.FirstOrDefaultAsync(x => x.BookingId == bookingId, cancellationToken);
        if (agreement is null || agreement.Status is AgreementStatus.Draft or AgreementStatus.ReadyForPickup) return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["agreement"] = ["Only a signed and agent-approved agreement can be emailed."] }));
        if (string.IsNullOrWhiteSpace(booking.Customer?.Email)) return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["email"] = ["The customer must have an email address before the agreement can be sent."] }));
        QueueAgreementEmail(agreement, booking);
        AuditWriter.Record(db, scope, "Rental agreement queued for email", nameof(RentalAgreement), agreement.Id, $"{agreement.AgreementNumber} queued to {agreement.LastEmailedTo}.", agreement.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { agreement.AgreementNumber, agreement.LastEmailedTo, agreement.LastEmailedAt, emailId = agreement.LastEmailId, status = EmailDeliveryStatus.Queued });
    }

    private async Task<Booking?> LoadBooking(Guid id, CancellationToken cancellationToken) => await db.Bookings
        .Include(item => item.Customer).Include(item => item.Branch).Include(item => item.Items).ThenInclude(item => item.Asset).ThenInclude(asset => asset!.Division).Include(item => item.Charges).Include(item => item.Inspections).Include(item => item.Payments).Include(item => item.Invoice).ThenInclude(invoice => invoice!.Lines).AsSplitQuery()
        .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    private static object BuildDraft(Booking booking)
    {
        var data = BuildAgreementData(booking);
        return new { approved = false, agreementNumber = (string?)null, termsVersion = TermsVersion,
            data.Customer, data.Asset, data.Rental, data.Pricing, data.Terms,
            customerSignatureName = (string?)null, customerSignedAt = (DateTimeOffset?)null,
            customerSignatureDataUrl = (string?)null, approvedByName = (string?)null, approvedAt = (DateTimeOffset?)null };
    }

    internal static object BuildApproved(RentalAgreement agreement, Booking booking)
    {
        var current = BuildAgreementData(booking);
        return new { approved = true, agreement.AgreementNumber, agreement.TermsVersion,
            Customer = DeserializeOrDefault(agreement.CustomerSnapshotJson, current.Customer),
            Asset = DeserializeOrDefault(agreement.AssetSnapshotJson, current.Asset), current.Rental,
            Pricing = DeserializeOrDefault(agreement.PricingSnapshotJson, current.Pricing),
            Terms = DeserializeOrDefault(agreement.TermsJson, current.Terms),
            agreement.CustomerSignatureName, agreement.CustomerSignatureDataUrl, agreement.CustomerSignedAt, agreement.ApprovedByName, agreement.ApprovedAt,
            agreement.LastEmailedTo, agreement.LastEmailedAt,
            settlement = new {
                stage = booking.Status == BookingStatus.Completed ? "Final return settlement" : "Current payment position",
                invoiceNumber = booking.Invoice?.InvoiceNumber,
                lines = booking.Invoice is not null && InvoiceDocumentLines.Read(booking.Invoice).Count > 0 ? InvoiceDocumentLines.Read(booking.Invoice) : RentalFinancials.Lines(booking),
                subtotal = booking.Invoice?.Subtotal ?? RentalFinancials.Net(booking),
                taxInclusive = booking.Invoice?.TaxInclusive ?? false,
                adjustment = booking.Invoice is null ? 0 : RentalFinancials.Money(booking.Invoice.Total - (booking.Invoice.TaxInclusive ? booking.Invoice.Subtotal : booking.Invoice.Subtotal + booking.Invoice.TaxAmount)),
                taxAmount = booking.Invoice?.TaxAmount ?? HirePricing.Tax(booking),
                total = booking.Invoice?.Total ?? RentalFinancials.Total(booking),
                paid = RentalFinancials.Paid(booking),
                bondAppliedToInvoice = booking.BondDeductionAmount,
                balanceDue = Math.Max(0, (booking.Invoice?.Total ?? RentalFinancials.Total(booking)) - RentalFinancials.Paid(booking)),
                booking.BondAmountHeld, booking.BondDeductionAmount, booking.BondRefundAmount, booking.BondDeductionReason,
                payments = booking.Payments.Where(x => x.Status == PaymentStatus.Recorded).Select(x => new { x.ReceiptNumber, type = x.Type.ToString(), method = x.Method.ToString(), x.Amount, x.CreatedAt })
            } };
    }

    private void QueueAgreementEmail(RentalAgreement agreement, Booking booking)
    {
        var customer = booking.Customer!; var current = BuildAgreementData(booking);
        var data = current with {
            Pricing = DeserializeOrDefault(agreement.PricingSnapshotJson, current.Pricing),
            Customer = DeserializeOrDefault(agreement.CustomerSnapshotJson, current.Customer),
            Asset = DeserializeOrDefault(agreement.AssetSnapshotJson, current.Asset),
            Terms = DeserializeOrDefault(agreement.TermsJson, current.Terms)
        };
        var pricingLines = string.Join("", (data.Pricing.Lines ?? []).Select(line => $"<tr><td>{HtmlEncoder.Default.Encode(line.Description)} ({line.Quantity:N2} × FJD {line.Rate:N2})</td><td>FJD {line.Amount:N2}</td></tr>"));
        var accountLines = booking.Invoice is not null ? InvoiceDocumentLines.Read(booking.Invoice) : RentalFinancials.Lines(booking);
        var accountRows = string.Join("", accountLines.Select(line => $"<tr><td>{HtmlEncoder.Default.Encode(line.Description)}</td><td>FJD {line.Amount:N2}</td></tr>"));
        var currentAccount = $"<h2>{(booking.Status == BookingStatus.Completed ? "Final return settlement" : "Current payment position")}</h2><p>The signed pickup schedule above is retained. The current account below includes recorded additional charges.</p><table>{accountRows}<tr><td>VAT</td><td>FJD {booking.Invoice?.TaxAmount ?? HirePricing.Tax(booking):N2}</td></tr><tr><td>Rental charge including VAT</td><td>FJD {booking.Invoice?.Total ?? RentalFinancials.Total(booking):N2}</td></tr><tr><td>Hire payments received</td><td>FJD {RentalFinancials.Paid(booking) - (booking.BondSettledAt.HasValue ? booking.BondDeductionAmount : 0):N2}</td></tr><tr><td>Bond applied to invoice</td><td>FJD {booking.BondDeductionAmount:N2}</td></tr><tr><td>Balance due</td><td>FJD {Math.Max(0, (booking.Invoice?.Total ?? RentalFinancials.Total(booking)) - RentalFinancials.Paid(booking)):N2}</td></tr><tr><td>Refundable bond collected</td><td>FJD {booking.BondAmountHeld:N2}</td></tr><tr><td>Bond refunded</td><td>FJD {booking.BondRefundAmount:N2}</td></tr></table>";
        var terms = string.Join("", data.Terms.Select(term => $"<h3 style=\"font-size:14px;margin:18px 0 5px\">{HtmlEncoder.Default.Encode(term.Title)}</h3><p style=\"font-size:13px;line-height:1.5;margin:0\">{HtmlEncoder.Default.Encode(term.Content)}</p>"));
        var content = $"<p>Dear {HtmlEncoder.Default.Encode(customer.Name)},</p><p>This is your signed and agent-approved rental agreement. Keep this email with your rental records.</p><table role=\"presentation\" width=\"100%\" style=\"background:#f5f5f2;padding:14px\"><tr><td><strong>Agreement</strong></td><td>{agreement.AgreementNumber}</td></tr><tr><td><strong>Booking</strong></td><td>{booking.BookingNumber}</td></tr><tr><td><strong>Asset</strong></td><td>{HtmlEncoder.Default.Encode(data.Asset.Name)} ({HtmlEncoder.Default.Encode(data.Asset.AssetNumber)})</td></tr><tr><td><strong>Rental period</strong></td><td>{data.Rental.StartAt:dd MMM yyyy} – {data.Rental.EndAt:dd MMM yyyy}</td></tr>{pricingLines}<tr><td><strong>Base hire</strong></td><td>FJD {data.Pricing.Subtotal:N2}</td></tr><tr><td><strong>Services and surcharges</strong></td><td>FJD {data.Pricing.AdditionalCharges:N2}</td></tr><tr><td><strong>Discount</strong></td><td>FJD {data.Pricing.DiscountAmount:N2}</td></tr><tr><td><strong>VAT</strong></td><td>FJD {data.Pricing.TaxAmount:N2}</td></tr><tr><td><strong>Rental charge including VAT</strong></td><td>FJD {data.Pricing.Total:N2}</td></tr><tr><td><strong>Refundable bond</strong></td><td>FJD {data.Pricing.DepositRequired:N2}</td></tr></table>{currentAccount}<h2 style=\"font-size:18px\">Terms and conditions</h2>{terms}<h2 style=\"font-size:18px\">Acceptance record</h2><p>Signed by <strong>{HtmlEncoder.Default.Encode(agreement.CustomerSignatureName)}</strong> on {agreement.CustomerSignedAt:dd MMM yyyy 'at' HH:mm}. Approved by <strong>{HtmlEncoder.Default.Encode(agreement.ApprovedByName)}</strong>. Terms version: {agreement.TermsVersion}.</p><p>Contact {HtmlEncoder.Default.Encode(booking.Branch?.Name ?? "the issuing branch")} on {HtmlEncoder.Default.Encode(booking.Branch?.Phone ?? "the published branch number")} if any detail is incorrect.</p>";
        var email = emailQueue.Queue(db, customer.Email!, $"Signed rental agreement {agreement.AgreementNumber}", EmailTemplate.Branded($"Rental agreement {agreement.AgreementNumber}", content), $"Signed rental agreement {agreement.AgreementNumber} for booking {booking.BookingNumber}. Asset {data.Asset.Name}. Total FJD {data.Pricing.Total:N2}.", "RentalAgreement");
        agreement.LastEmailedTo = customer.Email!.Trim(); agreement.LastEmailedAt = DateTimeOffset.UtcNow; agreement.LastEmailId = email.Id;
    }

    private static AgreementData BuildAgreementData(Booking booking)
    {
        var item = booking.Items.First();
        var days = Math.Max(1, (decimal)Math.Ceiling((item.EndAt - item.StartAt).TotalDays));
        var subtotal = booking.Items.Sum(HirePricing.Amount);
        var taxable = RentalFinancials.Net(booking);
        var tax = HirePricing.Tax(booking);
        return new AgreementData(
            new CustomerAgreementSnapshot(booking.CustomerId, booking.Customer!.CustomerNumber, booking.Customer.Name, booking.Customer.Type.ToString(), booking.Customer.Email, booking.Customer.Phone, booking.Customer.Address, booking.Customer.IdentificationNumber),
            new AssetAgreementSnapshot(item.AssetId, item.Asset!.AssetNumber, item.Asset.Name, item.Asset.Type.ToString(), item.Asset.RegistrationNumber, item.Asset.SerialNumber),
            new RentalAgreementSnapshot(booking.Id, booking.BookingNumber, booking.BranchId, booking.Branch!.Name, booking.Branch.Address, booking.Branch.Phone, item.StartAt, item.EndAt, days),
            new PricingAgreementSnapshot(item.DailyRate, subtotal, booking.DiscountAmount, RentalFinancials.Services(booking) + RentalFinancials.Unitemised(booking), booking.AdditionalChargesDescription, booking.TaxRate, tax, taxable + tax, booking.DepositRequired, item.HireUnit, HirePricing.Quantity(item), RentalFinancials.Lines(booking)),
            string.IsNullOrWhiteSpace(item.Asset.Division?.DefaultRentalTerms)
                ? DefaultTerms()
                : [new AgreementTerm("Rental terms", item.Asset.Division.DefaultRentalTerms)]);
    }

    private static IReadOnlyList<AgreementTerm> DefaultTerms() =>
    [
        new("1. Agreement and rental period", "This agreement, the booking summary, pricing schedule and signed inspection records form the entire rental record. The customer must return the asset to the stated branch by the agreed date and time unless Carpenters Rentals approves an extension in writing."),
        new("2. Charges, refundable bond and payment", "The customer must pay the stated rental charges, taxes, refundable bond and any properly assessed additional charges. Extra time, excess kilometres or hours, refuelling, cleaning, recovery, fines, tolls, loss and damage may be charged where applicable and disclosed."),
        new("3. Inspection and acceptance", "Before collection, the customer and rental officer must inspect the asset and record fuel, mileage or operating hours and existing damage. Signing confirms receipt in the recorded condition and suitability for the stated purpose, subject to defects that could not reasonably be identified."),
        new("4. Authorized drivers and operators", "Only persons approved by Carpenters Rentals who hold the legally required licence, permit, certification and competence may drive or operate the asset. The customer must not subhire, lend or transfer possession without written approval."),
        new("5. Safe and permitted use", "The asset must be used carefully, lawfully, within rated capacity and manufacturer instructions, only for the disclosed purpose and within any agreed geographic limits. Racing, towing without approval, overloading, unlawful use, use while impaired and reckless or unsafe operation are prohibited."),
        new("6. Care, fuel and routine checks", "The customer must secure the asset, use the correct fuel or power source and perform reasonable daily checks. No alteration or repair may be made without approval, except urgent action reasonably required to prevent injury or further damage."),
        new("7. Breakdown, accident, theft and damage", "The customer must stop using an unsafe asset and promptly contact Carpenters Rentals. Accidents, theft, loss and damage must be reported immediately, with police or other official reports obtained where required. The customer must not admit liability or arrange repairs without authorization."),
        new("8. Insurance and liability", "Any insurance, damage waiver, excess, exclusions and customer liability are only those expressly stated in the booking or an attached schedule. The customer remains responsible for excluded, unauthorized, reckless, unlawful or negligent use to the extent permitted by applicable law."),
        new("9. Return condition and inspection", "The asset must be returned with all supplied accessories and documents in substantially the same condition, fair wear and tear excepted, and at the agreed fuel or charge level. A return inspection may identify cleaning, fuel, late-return, missing-item or repair charges."),
        new("10. Fines and enforcement costs", "The customer is responsible for traffic, parking, toll, regulatory and operating penalties arising during the rental and reasonable administration costs connected with identifying the responsible driver or operator, where lawful."),
        new("11. Default and recovery", "Carpenters Rentals may terminate the rental and recover the asset where payment is overdue, information is materially false, use is unauthorized or unsafe, or the agreement is otherwise materially breached, subject to applicable law."),
        new("12. Privacy and records", "Customer information, identification, signatures, inspections and transaction records may be collected and used to administer the rental, protect assets, meet legal obligations and handle claims. Access must be limited to authorized purposes."),
        new("13. Governing law and disputes", "This agreement is governed by the laws of Fiji. The parties should first attempt to resolve concerns through the Carpenters Rentals branch. Nothing in this agreement excludes rights or remedies that cannot lawfully be excluded."),
        new("14. Acknowledgement", "The customer confirms that the details are accurate, the pricing and material terms were explained or made available before signing, questions could be asked, and a copy of the approved agreement will be provided."),
    ];

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, SnapshotJson)
        ?? throw new JsonException($"Stored rental agreement snapshot could not be read as {typeof(T).Name}.");
    private static T DeserializeOrDefault<T>(string json, T fallback)
    {
        try { return Deserialize<T>(json); }
        catch (JsonException) { return fallback; }
        catch (NotSupportedException) { return fallback; }
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private sealed record AgreementData(CustomerAgreementSnapshot Customer, AssetAgreementSnapshot Asset, RentalAgreementSnapshot Rental, PricingAgreementSnapshot Pricing, IReadOnlyList<AgreementTerm> Terms);
    private sealed record CustomerAgreementSnapshot(Guid CustomerId, string CustomerNumber, string Name, string Type, string? Email, string? Phone, string? Address, string? IdentificationNumber);
    private sealed record AssetAgreementSnapshot(Guid AssetId, string AssetNumber, string Name, string Type, string? RegistrationNumber, string? SerialNumber);
    private sealed record RentalAgreementSnapshot(Guid Id, string BookingNumber, Guid BranchId, string BranchName, string? BranchAddress, string? BranchPhone, DateTimeOffset StartAt, DateTimeOffset EndAt, decimal Days);
    private sealed record PricingAgreementSnapshot(decimal DailyRate, decimal Subtotal, decimal DiscountAmount, decimal AdditionalCharges, string? AdditionalChargesDescription, decimal TaxRate, decimal TaxAmount, decimal Total, decimal DepositRequired, ChargeUnit HireUnit, decimal HireQuantity, IReadOnlyList<FinancialLine>? Lines = null);
    private sealed record AgreementTerm(string Title, string Content);
}

public sealed record PickupRentalRequest(
    string ScannedAssetNumber,
    string CustomerSignatureName,
    bool CustomerAcceptedTerms,
    bool AgentApproved,
    bool IdentificationVerified,
    bool DriverLicenceVerified,
    bool PaymentVerified,
    string CustomerSignatureDataUrl,
    string? LicenceNumber,
    string? LicenceClass,
    DateOnly? LicenceExpiry,
    PaymentType PaymentType,
    PaymentMethod PaymentMethod,
    [System.ComponentModel.DataAnnotations.Range(0, 1000000)] decimal AmountCollected,
    string? ReceiptNumber,
    [System.ComponentModel.DataAnnotations.Range(0, 10000000)] decimal? MeterReading,
    [System.ComponentModel.DataAnnotations.Range(0, 100)] int? FuelLevelPercent,
    string? ConditionNotes,
    string? DamageNotes,
    IReadOnlyList<string>? EvidenceDataUrls,
    IReadOnlyList<string>? DamageZones,
    IReadOnlyList<string>? ChecklistItems, string? AccessoryNotes, IReadOnlyList<string>? ChecklistAllItems = null, IReadOnlyList<string>? ChecklistFailedItems = null)
{
    [System.ComponentModel.DataAnnotations.Range(0, 1000000)] public decimal RentalAmountCollected { get; init; }
    public bool PayHireOnReturn { get; init; }
    public string? PaymentDeferralReason { get; init; }
}
