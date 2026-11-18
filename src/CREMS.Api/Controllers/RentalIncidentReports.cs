using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

public sealed partial class RentalLifecycleController
{
    [HttpGet("incidents/context")]
    public async Task<ActionResult> IncidentContext(Guid bookingId, CancellationToken token)
    {
        var access = await GetBooking(bookingId, token); if (access.Result is not null) return access.Result;
        var booking = access.Booking!;
        if (booking.Items.Count != 1) return Conflict(new { message = "Select a hire with one allocated asset." });
        var asset = booking.Items.Single().Asset!;
        var config = await ConfiguredHireRules.Resolve(db, booking, token);
        return Ok(new { asset.Id, asset.AssetNumber, asset.Name, asset.CurrentMeterReading,
            tracksMeter = config?.TracksMeter ?? AssetCategoryPolicy.TracksMeter(asset),
            tracksFuel = config?.TracksFuel ?? AssetCategoryPolicy.TracksFuel(asset),
            meterUnit = config?.MeterUnit ?? asset.MeterUnit, customerName = booking.Customer!.Name,
            checks = IncidentInspection.Checks(AssetCategoryPolicy.IsVehicle(asset.Type)) });
    }

    [HttpGet("incidents/replacement-assets")]
    public async Task<ActionResult> IncidentReplacementAssets(Guid bookingId, CancellationToken token)
    {
        var access = await GetBooking(bookingId, token); if (access.Result is not null) return access.Result;
        var booking = access.Booking!;
        if (booking.Status != BookingStatus.ConvertedToRental || booking.IncidentClosedAt.HasValue || booking.Items.Count != 1)
            return Conflict(new { message = "A replacement requires an active single-asset hire." });
        var item = booking.Items.Single(); var now = DateTimeOffset.UtcNow;
        if (item.EndAt <= now) return Conflict(new { message = "Approve a rental extension before replacing an overdue asset." });
        var candidates = await db.Assets.AsNoTracking().Where(a => a.IsActive && a.Id != item.AssetId &&
            a.BranchId == booking.BranchId && a.AssetCategoryId == item.Asset!.AssetCategoryId &&
            a.ServiceOfferingId == item.Asset.ServiceOfferingId && a.Status == AssetStatus.Available).OrderBy(a => a.AssetNumber).ToListAsync(token);
        var choices = new List<object>();
        foreach (var candidate in candidates)
        {
            if (!access.Scope!.HasAssetAccess(candidate.BranchId, candidate.DivisionId) ||
                await ReplacementIssue(booking, candidate, now, token) is not null) continue;
            var capture = await ConfiguredHireRules.Capture(db, candidate, null, token);
            var template = capture.Templates.FirstOrDefault(t => t.Stage == InspectionStage.PreHire);
            choices.Add(new { candidate.Id, candidate.AssetNumber, candidate.Name, candidate.CurrentMeterReading,
                capture.TracksMeter, capture.TracksFuel, capture.MeterUnit,
                checklist = template is null ? new[] { "Identity and accessories checked", "Controls and safety checked", "Condition checked" }
                    : OrganizationConfiguration.Checklist(template.ChecklistJson).ToArray() });
        }
        return Ok(choices);
    }

    private async Task<string?> ReplacementIssue(Booking booking, Asset asset, DateTimeOffset now, CancellationToken token)
    {
        var proposed = new Booking { Id = booking.Id, BookingNumber = booking.BookingNumber, CustomerId = booking.CustomerId,
            Customer = booking.Customer, BranchId = booking.BranchId, Status = BookingStatus.Confirmed,
            Items = [new BookingItem { AssetId = asset.Id, Asset = asset, StartAt = now, EndAt = booking.Items.Single().EndAt }] };
        var problems = await PickupReadiness.Check(db, proposed, token);
        if (problems.Count > 0) return string.Join(" ", problems);
        if (await db.BookingItems.AnyAsync(i => i.BookingId != booking.Id && i.AssetId == asset.Id &&
            i.StartAt < proposed.Items.Single().EndAt.AddMinutes(asset.TurnaroundMinutes) && i.EndAt > now.AddMinutes(-asset.TurnaroundMinutes) &&
            (i.Booking!.Status == BookingStatus.Confirmed || i.Booking.Status == BookingStatus.ConvertedToRental ||
             i.Booking.Status == BookingStatus.Draft && i.Booking.CreatedAt > now.AddMinutes(-30)), token))
            return "The replacement is reserved during the remaining hire or turnaround period.";
        return null;
    }

    [HttpPost("incidents/report")]
    [HttpPost("incidents/{incidentId:guid}/report")]
    public async Task<ActionResult> SaveIncidentReport(Guid bookingId, [FromRoute] Guid? incidentId, StaffIncidentReport request, CancellationToken token)
    {
        await using var transaction = await MaintenanceRules.BeginAsync(db, token);
        var access = await GetBooking(bookingId, token); if (access.Result is not null) return access.Result;
        var booking = access.Booking!; var scope = access.Scope!; var now = DateTimeOffset.UtcNow;
        if (booking.Status != BookingStatus.ConvertedToRental || booking.IncidentClosedAt.HasValue || booking.Items.Count != 1)
            return Conflict(new { message = "Incident actions require an active hire with one allocated asset." });
        var item = booking.Items.Single(); var asset = item.Asset!;
        if (request.ExpectedAssetId != asset.Id || asset.Status != AssetStatus.Rented ||
            await db.RentalInspections.AnyAsync(x => x.BookingId == bookingId && x.Type == InspectionType.Return, token))
            return Conflict(new { message = "The allocation or return state changed. Reload the hire before recording the incident." });
        RentalIncident? incident = incidentId.HasValue
            ? await db.RentalIncidents.SingleOrDefaultAsync(x => x.Id == incidentId && x.BookingId == bookingId, token) : null;
        if (incidentId.HasValue && incident is null) return NotFound();
        if (incident is not null && (incident.ReviewedAt != request.ExpectedReviewedAt ||
            incident.Outcome is RentalIncidentOutcome.ReplaceAsset or RentalIncidentOutcome.EndHire ||
            incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed || incident.AssetId.HasValue && incident.AssetId != asset.Id))
            return Conflict(new { message = "This incident changed, was finalized, or belongs to the previous asset. Reopen its current record." });
        if (!Enum.IsDefined(request.Outcome) || !Enum.IsDefined(request.Type)) return Validation("outcome", "Choose a valid incident type and rental outcome.");
        if (request.OccurredAt == default || request.OccurredAt > now || request.OccurredAt < item.StartAt)
            return Validation("occurredAt", "Use an incident time during this hire, no later than now.");
        if (incident is null && request.Description?.Trim().Length is not >= 10 || request.Assessment?.Trim().Length is not >= 10 || request.Location?.Trim().Length is not >= 3)
            return Validation("description", "Describe the incident and staff assessment in at least 10 characters, and give its location.");
        if (request.EstimatedCost is < 0 or > 1000000 || request.EstimatedCost != RentalFinancials.Money(request.EstimatedCost))
            return Validation("estimatedCost", "Enter an estimated cost in dollars and cents between zero and FJD 1,000,000.");
        if (IncidentEvidence.Validate(request.EvidenceDataUrls) is { } evidenceError) return Validation("evidence", evidenceError);
        var previousEvidence = incident is null ? [] : SharedRentalIncidents.Evidence(incident);
        if (previousEvidence.Any(file => !request.EvidenceDataUrls.Contains(file)))
            return Validation("evidence", "Keep the original customer/staff attachments with the incident; add new evidence alongside them.");
        var configuration = await ConfiguredHireRules.Resolve(db, booking, token);
        var checks = IncidentInspection.Checks(AssetCategoryPolicy.IsVehicle(asset.Type));
        if (request.Checks.Count != checks.Length || checks.Any(check => !request.Checks.TryGetValue(check.Code, out var result) || result is not ("Passed" or "Issue" or "NotInspected")))
            return Validation("checks", "Record a result for every quick inspection check.");
        if (request.MeterReading is < 0 or > 10000000 || request.FuelPercent is < 0 or > 100)
            return Validation("meterReading", "Enter a valid meter reading and fuel level from zero to 100 percent.");
        if (request.Outcome != RentalIncidentOutcome.EndHire &&
            (configuration?.TracksMeter == true && request.MeterReading is null || configuration?.TracksFuel == true && request.FuelPercent is null))
            return Validation("meterReading", "Record the required meter and fuel readings, or use incident closure when inspection is impossible.");
        if (request.MeterReading.HasValue && asset.CurrentMeterReading.HasValue && request.MeterReading < asset.CurrentMeterReading)
            return Validation("meterReading", "The incident meter reading cannot be below the asset's last recorded reading.");
        if (!request.Confirmed) return Validation("confirmed", "Confirm the selected rental outcome and customer arrangement.");
        if (request.Outcome == RentalIncidentOutcome.ContinueRental &&
            (!request.SafeToContinue || (incident?.Type ?? request.Type) == IncidentType.Theft || checks.Where(c => c.SafetyCritical).Any(c => request.Checks[c.Code] != "Passed")))
            return Validation("safeToContinue", "Keep the hire active only after all safety checks pass and staff confirm it is safe. Otherwise replace the asset or end the hire.");
        if (request.Outcome != RentalIncidentOutcome.ContinueRental &&
            (request.AssetStatus is not (AssetStatus.Maintenance or AssetStatus.OutOfService) || request.Custody?.Trim().Length is not >= 3))
            return Validation("custody", "Select Needs repair or Out of service and record the damaged asset's location or custody.");
        Asset? replacement = null; IncidentReplacementHandover? handover = null; BookingConfigurationSnapshot? replacementConfig = null;
        if (request.Outcome == RentalIncidentOutcome.ReplaceAsset)
        {
            if (item.EndAt <= now) return Validation("replacementAssetId", "Approve a rental extension before replacing an overdue asset.");
            replacement = await db.Assets.Include(a => a.AssetCategory).Include(a => a.ServiceOffering).Include(a => a.Division)
                .SingleOrDefaultAsync(a => a.Id == request.ReplacementAssetId, token);
            if (replacement is null || replacement.Id == asset.Id || !replacement.IsActive || replacement.Status != AssetStatus.Available ||
                replacement.BranchId != booking.BranchId || replacement.ServiceOfferingId != asset.ServiceOfferingId ||
                replacement.AssetCategoryId != asset.AssetCategoryId || !scope.HasAssetAccess(replacement.BranchId, replacement.DivisionId))
                return Validation("replacementAssetId", "Choose an available replacement from the same branch, service and category.");
            if (await ReplacementIssue(booking, replacement, now, token) is { } problem) return Conflict(new { message = problem });
            if (AssetCategoryPolicy.IsVehicle(replacement.Type) && !booking.Charges.Any(c => c.Category is ChargeCategory.Driver or ChargeCategory.Operator))
            {
                var today = MaintenanceRules.LocalDate(now);
                if (!await db.AuthorizedDrivers.AnyAsync(d => d.BookingId == bookingId && d.Verified && d.LicenceExpiry > today, token))
                    return Validation("driverLicence", "Verify an unexpired authorized driver licence before handing over the replacement vehicle.");
            }
            handover = request.ReplacementHandover;
            if (handover is null || !handover.SafeToOperate || !handover.CustomerAccepted || handover.CustomerName?.Trim().Length is not >= 2 ||
                string.IsNullOrWhiteSpace(handover.SignatureDataUrl) || !handover.SignatureDataUrl.StartsWith("data:image/png;base64,") ||
                IncidentEvidence.Validate([handover.SignatureDataUrl]) is not null || handover.Photos.Count == 0 ||
                handover.Photos.Any(p => !p.StartsWith("data:image/")) || IncidentEvidence.Validate(handover.Photos) is not null)
                return Validation("replacementHandover", "Record the replacement's safety check, customer acceptance, name, drawn signature and at least one valid photo.");
            replacementConfig = await ConfiguredHireRules.Capture(db, replacement, null, token);
            var template = replacementConfig.Templates.FirstOrDefault(t => t.Stage == InspectionStage.PreHire);
            var required = template is null ? new[] { "Identity and accessories checked", "Controls and safety checked", "Condition checked" }
                : OrganizationConfiguration.Checklist(template.ChecklistJson).ToArray();
            if (handover.PassedChecks.Count != required.Length || required.Except(handover.PassedChecks).Any() ||
                handover.MeterReading is < 0 or > 10000000 || handover.MeterReading < replacement.CurrentMeterReading || handover.FuelPercent is < 0 or > 100 ||
                replacementConfig.TracksMeter && handover.MeterReading is null || replacementConfig.TracksFuel && handover.FuelPercent is null)
                return Validation("replacementHandover", "Pass every replacement checklist item and enter valid starting meter/fuel readings.");
            if (await db.RentalAgreements.AnyAsync(x => x.BookingId == bookingId && x.Status == AgreementStatus.Completed, token))
                return Conflict(new { message = "The agreement is already closed. Reload the hire." });
        }
        var previous = incident is null ? null : JsonSerializer.Serialize(new { incident.Description, incident.InspectionJson, incident.EvidenceJson, incident.Outcome });
        if (incident is null)
        {
            incident = new RentalIncident { BookingId = bookingId, IncidentNumber = $"INC-{now:yyyy}-{Guid.NewGuid():N}"[..26],
                Description = request.Description.Trim(), Type = request.Type, OccurredAt = request.OccurredAt, AssetId = asset.Id };
            db.RentalIncidents.Add(incident);
        }
        // Original customer narrative and occurrence time are retained when staff assess a report.
        incident.AssetId ??= asset.Id;
        incident.Location = Clean(request.Location); incident.PoliceReference = Clean(request.PoliceReference) ?? incident.PoliceReference;
        incident.InsuranceClaimNumber = Clean(request.InsuranceClaimNumber) ?? incident.InsuranceClaimNumber;
        incident.EstimatedCost = request.EstimatedCost;
        incident.EvidenceJson = JsonSerializer.Serialize(request.EvidenceDataUrls.Distinct());
        incident.InspectionJson = JsonSerializer.Serialize(new { request.Checks, request.MeterReading, request.FuelPercent,
            assessment = request.Assessment.Trim(), request.SafeToContinue, custody = Clean(request.Custody), asset.AssetNumber,
            recordedAt = now, recordedBy = scope.UserName }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        incident.Outcome = request.Outcome; incident.ReviewedAt = now; incident.ReviewedByUserId = scope.UserId;
        incident.ReviewedByName = scope.UserName; if (incident.Status == IncidentStatus.Open) incident.Status = IncidentStatus.Investigating;
        incident.StaffResolution = request.Assessment.Trim();
        if (request.Outcome == RentalIncidentOutcome.EndHire)
        {
            var inspection = new AssetInspection { AssetId = asset.Id, BookingId = bookingId, Stage = InspectionStage.Maintenance,
                Outcome = InspectionOutcome.DamageDetected, ResponsesJson = JsonSerializer.Serialize(request.Checks),
                EvidenceJson = incident.EvidenceJson!, Notes = $"Incident condition assessment: {incident.IncidentNumber}. {request.Assessment.Trim()}",
                MeterReading = request.MeterReading, FuelPercent = request.FuelPercent, CompletedAt = now,
                CompletedByUserId = scope.UserId, CompletedByName = scope.UserName };
            db.AssetInspections.Add(inspection);
            await EndIncidentHireCore(booking, asset, incident, request.AssetStatus!.Value, request.Assessment.Trim(), request.Custody!.Trim(), scope, now, token, inspection.Id);
        }
        else if (replacement is not null)
            await ReplaceIncidentAsset(booking, asset, replacement, incident, handover!, replacementConfig!, request, scope, now, token);
        else
        {
            incident.ImmediateReturnRequested = false;
            incident.CustomerResolution = "The branch has inspected the reported issue and agreed the hire can remain active. Return the asset through the normal return process; contact the branch if the condition changes.";
            if (request.MeterReading.HasValue) asset.CurrentMeterReading = request.MeterReading;
        }
        booking.UpdatedAt = now;
        await SharedRentalIncidents.SyncCase(db, booking, incident, token);
        AuditWriter.Record(db, scope, "Incident report and rental outcome recorded", nameof(Booking), bookingId,
            $"{incident.IncidentNumber}: {request.Outcome}.", booking.BranchId, previous,
            JsonSerializer.Serialize(new { incident.Outcome, incident.AssetId, incident.ReplacementAssetId, incident.InspectionJson }));
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return Ok(new { incident.Id, incident.IncidentNumber, outcome = request.Outcome.ToString(), booking.Status, booking.IncidentClosedAt });
    }

    private async Task ReplaceIncidentAsset(Booking booking, Asset oldAsset, Asset replacement, RentalIncident incident,
        IncidentReplacementHandover handover, BookingConfigurationSnapshot config, StaffIncidentReport request, StaffDataScope scope,
        DateTimeOffset now, CancellationToken token)
    {
        var item = booking.Items.Single(); var oldId = oldAsset.Id;
        var note = $"{incident.IncidentNumber}: {oldAsset.AssetNumber} replaced by {replacement.AssetNumber}. {request.Assessment.Trim()}";
        var damageInspection = new AssetInspection { AssetId = oldId, BookingId = booking.Id, Stage = InspectionStage.PostHire,
            Outcome = InspectionOutcome.DamageDetected, ResponsesJson = JsonSerializer.Serialize(request.Checks),
            EvidenceJson = incident.EvidenceJson!, MeterReading = request.MeterReading, FuelPercent = request.FuelPercent,
            Notes = note + " Custody: " + request.Custody, CompletedByUserId = scope.UserId, CompletedByName = scope.UserName, CompletedAt = now };
        db.AssetInspections.Add(damageInspection);
        db.AssetInspections.Add(new AssetInspection { AssetId = replacement.Id, BookingId = booking.Id, Stage = InspectionStage.PreHire,
            Outcome = InspectionOutcome.Passed, ResponsesJson = JsonSerializer.Serialize(handover.PassedChecks),
            EvidenceJson = JsonSerializer.Serialize(new { checklist = handover.PassedChecks, photos = handover.Photos }),
            MeterReading = handover.MeterReading, FuelPercent = handover.FuelPercent, CustomerSignatureName = handover.CustomerName.Trim(),
            CustomerSignatureDataUrl = handover.SignatureDataUrl, StaffSignatureName = scope.UserName,
            Notes = "Incident replacement handover: " + note, CompletedByUserId = scope.UserId, CompletedByName = scope.UserName, CompletedAt = now });
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = oldId, BookingId = booking.Id, Type = AssetLifecycleEventType.DamageReported,
            FromStatus = oldAsset.Status, ToStatus = request.AssetStatus!.Value, OccurredAt = now, MeterReading = request.MeterReading,
            Notes = note, RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = replacement.Id, BookingId = booking.Id, Type = AssetLifecycleEventType.CheckedOut,
            FromStatus = replacement.Status, ToStatus = AssetStatus.Rented, OccurredAt = now, MeterReading = handover.MeterReading,
            Notes = note, RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        if (request.AssetStatus == AssetStatus.Maintenance)
            db.MaintenanceJobs.Add(new MaintenanceJob { JobNumber = $"MNT-{Guid.NewGuid():N}"[..26], AssetId = oldId, BranchId = oldAsset.BranchId,
                ServiceType = "Incident damage assessment", FaultDescription = request.Assessment.Trim(), SourceType = "Damage",
                SourceReference = incident.IncidentNumber, SourceInspectionId = damageInspection.Id, Priority = MaintenancePriority.High, ReportedAt = now, ReportedByName = scope.UserName });
        oldAsset.Status = request.AssetStatus!.Value; oldAsset.UpdatedAt = now;
        if (request.MeterReading.HasValue) oldAsset.CurrentMeterReading = request.MeterReading;
        replacement.Status = AssetStatus.Rented; replacement.UpdatedAt = now;
        if (handover.MeterReading.HasValue) replacement.CurrentMeterReading = handover.MeterReading;
        if (request.MeterReading is { } oldReading)
            db.AssetMeterReadings.Add(new AssetMeterReading { AssetId = oldId, BookingId = booking.Id, Reading = oldReading,
                Type = AssetCategoryPolicy.MeterKind(oldAsset), Unit = oldAsset.MeterUnit ?? "units", FuelPercent = request.FuelPercent,
                Source = MeterReadingSource.PostHireInspection, RecordedByUserId = scope.UserId, RecordedAt = now });
        if (handover.MeterReading is { } replacementReading)
            db.AssetMeterReadings.Add(new AssetMeterReading { AssetId = replacement.Id, BookingId = booking.Id, Reading = replacementReading,
                Type = AssetCategoryPolicy.MeterKind(replacement), Unit = replacement.MeterUnit ?? "units", FuelPercent = handover.FuelPercent,
                Source = MeterReadingSource.PreHireInspection, RecordedByUserId = scope.UserId, RecordedAt = now });
        item.AssetId = replacement.Id; item.Asset = replacement;
        // Same agreement schedule, dates, price and bond: substitution is not another charge.
        booking.ConfigurationSnapshotJson = JsonSerializer.Serialize(config with {
            Answers = OrganizationConfiguration.Read<BookingConfigurationSnapshot>(booking.ConfigurationSnapshotJson)?.Answers ?? new Dictionary<string,string>() }, OrganizationConfiguration.Json);
        booking.Notes = string.Join("\n", new[] { booking.Notes, "Asset replaced: " + note });
        incident.ReplacementAssetId = replacement.Id; incident.ImmediateReturnRequested = false;
        incident.CustomerResolution = $"The branch replaced {oldAsset.AssetNumber} with {replacement.AssetNumber}. Your booking, agreed rental charges and held bond continue. Return the replacement through the normal return process.";
        var agreement = await db.RentalAgreements.SingleOrDefaultAsync(x => x.BookingId == booking.Id, token);
        if (agreement is not null)
            db.RentalAgreementAddendums.Add(new RentalAgreementAddendum { RentalAgreementId = agreement.Id,
                AddendumNumber = $"{agreement.AgreementNumber}-A{await db.RentalAgreementAddendums.CountAsync(x => x.RentalAgreementId == agreement.Id, token)+1}",
                Reason = note, ChangesJson = JsonSerializer.Serialize(new { originalAssetId = oldId, replacementAssetId = replacement.Id,
                    replacement.AssetNumber, effectiveAt = now, handover.SignatureDataUrl, agreedRateUnchanged = true, bondUnchanged = true }),
                CustomerSignatureName = handover.CustomerName.Trim(), CustomerSignedAt = now, ApprovedByUserId = scope.UserId, ApprovedByName = scope.UserName });
    }
}

public sealed record IncidentCheck(string Code, string Label, bool SafetyCritical);
public static class IncidentInspection
{
    public static IncidentCheck[] Checks(bool vehicle) => vehicle
        ? [new("body", "Body, glass and visible damage", false), new("brakes", "Tyres, steering and brakes", true),
           new("safety", "Lights, seatbelts and safety equipment", true), new("operation", "Engine, leaks and warning lights", true)]
        : [new("body", "Casing, structure and visible damage", false), new("guards", "Guards and safety devices", true),
           new("accessories", "Cables, attachments and accessories", true), new("operation", "Controls, leaks and operation", true)];
}
public sealed record StaffIncidentReport(
    Guid ExpectedAssetId, DateTimeOffset? ExpectedReviewedAt, RentalIncidentOutcome Outcome, IncidentType Type,
    DateTimeOffset OccurredAt, [Required, MaxLength(2000)] string Description,
    [Required, StringLength(500, MinimumLength=3)] string Location, [StringLength(100)] string? PoliceReference,
    [StringLength(100)] string? InsuranceClaimNumber, decimal EstimatedCost,
    [Required, MaxLength(5)] IReadOnlyList<string> EvidenceDataUrls,
    [Required] IReadOnlyDictionary<string,string> Checks, decimal? MeterReading, int? FuelPercent,
    [Required, StringLength(1000, MinimumLength=10)] string Assessment, bool SafeToContinue, bool Confirmed,
    AssetStatus? AssetStatus = null, [StringLength(300)] string? Custody = null, Guid? ReplacementAssetId = null,
    IncidentReplacementHandover? ReplacementHandover = null);
public sealed record IncidentReplacementHandover([Required] IReadOnlyList<string> PassedChecks, decimal? MeterReading,
    int? FuelPercent, [Required, MaxLength(5)] IReadOnlyList<string> Photos, bool SafeToOperate, bool CustomerAccepted,
    [Required, StringLength(150)] string CustomerName, [Required] string SignatureDataUrl);
