using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/maintenance-jobs")]
[Authorize(Policy = SystemPolicies.ManageMaintenance)]
public sealed class MaintenanceJobsController(ApplicationDbContext db, CurrentStaffScope staffScope, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> GetAll(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && scope.BranchIds.Count == 0)) return Forbid();
        var query = db.MaintenanceJobs.AsNoTracking();
        if (!scope.IsAdministrator) query = query.Where(job => scope.BranchIds.Contains(job.BranchId) && job.Asset!.DivisionId.HasValue && scope.DivisionIds.Contains(job.Asset.DivisionId.Value));
        var financial = (await authorization.AuthorizeAsync(User, SystemPermissions.AssetsViewFinancials)).Succeeded;
        var jobs = await query.Include(x => x.Asset).Include(x => x.Branch).OrderByDescending(x => x.ReportedAt).ToListAsync(cancellationToken);
        return Ok(jobs.Select(x => MaintenanceWorkspace.Record(x, financial)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult> Create(SaveMaintenanceJobRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await MaintenanceRules.BeginAsync(db, cancellationToken);
        var asset = await db.Assets.Include(item => item.Branch).FirstOrDefaultAsync(item => item.Id == request.AssetId, cancellationToken);
        if (asset is null) return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["assetId"] = ["Select a valid asset."] }));
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        var referenceError = await ValidateReferences(asset.Id, null, request.SupplierId, request.ParentFailureJobId, cancellationToken);
        if (referenceError is not null) return referenceError;
        if (!asset.IsActive || asset.Status is not (AssetStatus.Available or AssetStatus.Maintenance or AssetStatus.OutOfService))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["assetId"] = ["Only available assets or assets already in maintenance can enter maintenance. Resolve any hire, reservation or inspection first."] }));
        if (request.SourceType is not ("Manual" or "Inspection" or "Damage" or "Preventive")) return BadRequest(new { message = "Select a valid job source." });
        if (!string.IsNullOrWhiteSpace(request.SourceType) && request.SourceType is "Inspection" or "Damage" && !request.SourceInspectionId.HasValue && string.IsNullOrWhiteSpace(request.SourceReference)) return BadRequest(new { message = "Provide the originating inspection or damage reference." });
        if (request.SourceInspectionId.HasValue && !await db.AssetInspections.AnyAsync(x => x.Id == request.SourceInspectionId && x.AssetId == asset.Id, cancellationToken)) return BadRequest(new { message = "Select an inspection for this asset." });
        if (request.EstimatedCost > 0 && !(await authorization.AuthorizeAsync(User, SystemPermissions.AssetsViewFinancials)).Succeeded) return Forbid();
        if (request.AssignedPersonnelId.HasValue) {
            var technician = await db.Personnel.SingleOrDefaultAsync(x => x.Id == request.AssignedPersonnelId && x.IsActive && x.Type == PersonnelType.Technician && x.BranchId == asset.BranchId && x.DivisionId == asset.DivisionId, cancellationToken);
            if (technician is null || technician.Availability is PersonnelAvailability.Leave or PersonnelAvailability.Unavailable or PersonnelAvailability.Training) return BadRequest(new { message = "Select an active technician in this asset's branch and division." });
            request = request with { AssignedTo = technician.FullName };
        }
        var job = new MaintenanceJob { JobNumber = $"MNT-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = request.ServiceType.Trim(),
            FaultDescription = request.FaultDescription.Trim(), AssignedTo = Normalize(request.AssignedTo),
            Supplier = Normalize(request.Supplier), SupplierId = request.SupplierId, UseDetailedCosts = true, HasEstimate = request.HasEstimate ?? true, EstimatedCost = request.EstimatedCost, IsPreventive = request.IsPreventive, NextServiceMeter = request.NextServiceMeter, WarrantyCovered = request.WarrantyCovered, WarrantyClaimNumber = Normalize(request.WarrantyClaimNumber), ParentFailureJobId = request.ParentFailureJobId,
            AssignedPersonnelId = request.AssignedPersonnelId, SourceType = request.SourceType, SourceInspectionId = request.SourceInspectionId,
            SourceReference = Normalize(request.SourceReference), ReportedByName = scope.UserName, ExpectedReleaseAt = request.ExpectedReleaseAt, ServiceIntervalMonths = request.ServiceIntervalMonths,
            NextServiceDate = request.NextServiceDate, Priority = request.Priority, Status = MaintenanceStatus.Open };
        var previousAssetStatus = asset.Status;
        asset.Status = AssetStatus.Maintenance;
        db.MaintenanceJobs.Add(job);
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, Type = AssetLifecycleEventType.MaintenanceStarted, FromStatus = previousAssetStatus, ToStatus = AssetStatus.Maintenance, Notes = job.FaultDescription, RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        AuditWriter.Record(db, scope, "Maintenance logged", "MaintenanceJob", job.Id,
            $"{job.JobNumber} opened for {asset.AssetNumber}.", asset.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Ok(new { job.Id, job.JobNumber });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, UpdateMaintenanceJobRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await MaintenanceRules.BeginAsync(db, cancellationToken);
        var job = await db.MaintenanceJobs.Include(item => item.Asset).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (job is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(job.BranchId, job.Asset?.DivisionId)) return Forbid();
        if (request.ExpectedVersion.HasValue && request.ExpectedVersion != (job.UpdatedAt ?? job.CreatedAt))
            return Conflict(new { message = "This maintenance job changed since you opened it. Refresh and reopen the job before saving." });
        if ((request.Status == MaintenanceStatus.Completed || job.Status == MaintenanceStatus.Completed)
            && !(await authorization.AuthorizeAsync(User, SystemPermissions.MaintenanceComplete)).Succeeded) return Forbid();
        var referenceError = await ValidateReferences(job.AssetId, job.Id, request.SupplierId, request.ParentFailureJobId, cancellationToken);
        if (referenceError is not null) return referenceError;
        var reopening = job.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled && request.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled);
        if (reopening && !(await authorization.AuthorizeAsync(User, SystemPermissions.MaintenanceComplete)).Succeeded) return Forbid();
        if ((reopening || request.Status == MaintenanceStatus.Cancelled && job.Status != MaintenanceStatus.Cancelled || request.Status == MaintenanceStatus.WaitingForParts && job.Status != MaintenanceStatus.WaitingForParts) && string.IsNullOrWhiteSpace(request.TransitionReason)) return BadRequest(new { message = "Enter a reason for this status change." });
        if (job.Status == MaintenanceStatus.Cancelled && request.Status == MaintenanceStatus.Completed) return BadRequest(new { message = "Reopen cancelled work before completing it." });
        if (request.AssignedPersonnelId.HasValue) {
            var technician = await db.Personnel.SingleOrDefaultAsync(x => x.Id == request.AssignedPersonnelId && x.IsActive && x.Type == PersonnelType.Technician && x.BranchId == job.BranchId && x.DivisionId == job.Asset!.DivisionId, cancellationToken);
            if (technician is null || technician.Availability is PersonnelAvailability.Leave or PersonnelAvailability.Unavailable or PersonnelAvailability.Training) return BadRequest(new { message = "Select an available technician for this branch and division." });
            request = request with { AssignedTo = technician.FullName };
        }
        var financial = (await authorization.AuthorizeAsync(User, SystemPermissions.AssetsViewFinancials)).Succeeded;
        if (!financial) request = request with { HasEstimate = job.HasEstimate, EstimatedCost = job.EstimatedCost, ActualCost = job.ActualCost, PartsCost = job.PartsCost, LabourCost = job.LabourCost, TransportCost = job.TransportCost, ExternalServiceCost = job.ExternalServiceCost, TaxCost = job.TaxCost, OtherCost = job.OtherCost, FuelCost = job.FuelCost, LabourHours = job.LabourHours, LabourRate = job.LabourRate, InvoiceNumber = job.InvoiceNumber, UseDetailedCosts = job.UseDetailedCosts };
        if (request.LabourHours.HasValue != request.LabourRate.HasValue) return BadRequest(new { message = "Enter both labour hours and hourly rate, or leave both blank." });
        if (request.LabourHours.HasValue) request = request with { LabourCost = decimal.Round(request.LabourHours.Value * request.LabourRate!.Value, 2) };
        var completing = request.Status == MaintenanceStatus.Completed && job.Status != MaintenanceStatus.Completed;
        if (completing && request.CalculateDowntime) request = request with { DowntimeHours = (int)Math.Min(100000, Math.Max(0, Math.Ceiling((DateTimeOffset.UtcNow - job.ReportedAt).TotalHours))) };
        if (completing && string.IsNullOrWhiteSpace(request.CompletionNotes)) return BadRequest(new { message = "Record the work performed before completing this job." });
        if (completing && !string.IsNullOrWhiteSpace(job.Asset?.MeterUnit) && !request.MeterReading.HasValue) return BadRequest(new { message = "Record the final meter reading before completion." });
        if (completing && request.ServiceIntervalMonths.HasValue && !request.NextServiceDate.HasValue) request = request with { NextServiceDate = MaintenanceRules.LocalDate(DateTimeOffset.UtcNow).AddMonths(request.ServiceIntervalMonths.Value) };
        var meterChanged = request.MeterReading != job.MeterReading;
        var scheduleChanged = request.NextServiceDate != job.NextServiceDate;
        var previousServiceDate = job.NextServiceDate;
        var issuedCost = await db.MaintenancePartUsages.Where(item => item.MaintenanceJobId == id)
            .SumAsync(item => (decimal?)(item.Quantity * item.UnitCost), cancellationToken) ?? 0;
        if (request.PartsCost < issuedCost)
            return BadRequest(new { message = "Parts cost cannot be less than the net cost of inventory issued to this job. Return unused stock before reducing its cost." });
        if (request.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled) && job.Asset?.BranchId != job.BranchId)
            return BadRequest(new { message = "Create a new maintenance job at the asset's current branch instead of reopening a transferred asset's historical job." });
        if (completing && (request.IsPreventive || job.IsPreventive || job.Asset?.NextServiceDate <= MaintenanceRules.LocalDate(DateTimeOffset.UtcNow)) &&
            ((job.Asset?.NextServiceDate.HasValue == true && !request.NextServiceDate.HasValue) || (job.NextServiceMeter.HasValue && !request.NextServiceMeter.HasValue)
            || (request.NextServiceDate.HasValue && request.NextServiceDate <= MaintenanceRules.LocalDate(DateTimeOffset.UtcNow))
            || (request.NextServiceMeter.HasValue && request.NextServiceMeter <= (request.MeterReading ?? job.Asset?.CurrentMeterReading))))
            return BadRequest(new { message = "Set the next preventive service date and meter threshold beyond today and the current reading." });
        if (request.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
            && (job.Asset is null || !job.Asset.IsActive || job.Asset.Status is not (AssetStatus.Available or AssetStatus.Maintenance or AssetStatus.Inspection or AssetStatus.OutOfService)))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["This asset cannot enter maintenance in its current state."] }));
        if (request.Status == MaintenanceStatus.Completed && (completing || meterChanged) && request.MeterReading.HasValue
            && job.Asset?.CurrentMeterReading > request.MeterReading)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["meterReading"] = ["The meter reading cannot be lower than the asset's current reading."] }));
        var previous = $"Status: {job.Status}; Actual cost: {job.ActualCost:0.00}";
        if (request.Status == MaintenanceStatus.InProgress && job.StartedAt == null) job.StartedAt = DateTimeOffset.UtcNow;
        if (reopening) { job.ReleasedAt = null; job.ReleasedByName = null; job.ReleaseInspectionId = null; job.StartedAt = request.Status == MaintenanceStatus.InProgress ? DateTimeOffset.UtcNow : null; }
        job.AssignedPersonnelId = request.AssignedPersonnelId; job.ExpectedReleaseAt = request.ExpectedReleaseAt;
        job.LabourHours = request.LabourHours; job.LabourRate = request.LabourRate; job.FuelCost = request.FuelCost; job.ServiceIntervalMonths = request.ServiceIntervalMonths;
        job.Priority = request.Priority; job.Description = Normalize(request.CompletionNotes);
        job.Status = request.Status; job.ServiceType = request.ServiceType.Trim();
        job.FaultDescription = request.FaultDescription.Trim(); job.AssignedTo = Normalize(request.AssignedTo);
        job.Supplier = Normalize(request.Supplier); job.HasEstimate = request.HasEstimate ?? job.HasEstimate; job.EstimatedCost = request.EstimatedCost;
        job.SupplierId = request.SupplierId; job.IsPreventive = request.IsPreventive; job.NextServiceMeter = request.NextServiceMeter; job.WarrantyCovered = request.WarrantyCovered; job.WarrantyClaimNumber = Normalize(request.WarrantyClaimNumber); job.ParentFailureJobId = request.ParentFailureJobId;
        var hadDetailedCosts = job.PartsCost + job.LabourCost + job.TransportCost + job.ExternalServiceCost + job.TaxCost + job.OtherCost + job.FuelCost > 0;
        job.PartsCost = request.PartsCost; job.LabourCost = request.LabourCost; job.TransportCost = request.TransportCost;
        job.ExternalServiceCost = request.ExternalServiceCost; job.TaxCost = request.TaxCost; job.OtherCost = request.OtherCost;
        var detailedTotal = request.PartsCost + request.LabourCost + request.TransportCost + request.ExternalServiceCost + request.TaxCost + request.OtherCost + request.FuelCost;
        job.ActualCost = (request.UseDetailedCosts ?? job.UseDetailedCosts ?? hadDetailedCosts) || detailedTotal > 0 ? detailedTotal : request.ActualCost;
        job.UseDetailedCosts = request.UseDetailedCosts ?? job.UseDetailedCosts;
        job.PartsUsed = Normalize(request.PartsUsed); job.InvoiceNumber = Normalize(request.InvoiceNumber); job.MeterReading = request.MeterReading; job.DowntimeHours = request.DowntimeHours;
        job.NextServiceDate = request.NextServiceDate; job.UpdatedAt = DateTimeOffset.UtcNow;
        var closed = request.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled;
        job.CompletedAt = request.Status == MaintenanceStatus.Completed
            ? job.CompletedAt ?? DateTimeOffset.UtcNow : null;
        if (job.Asset is not null)
        {
            var asset = job.Asset;
            var otherOpenJobs = await db.MaintenanceJobs.AnyAsync(item => item.AssetId == job.AssetId && item.Id != job.Id
                && item.Status != MaintenanceStatus.Completed && item.Status != MaintenanceStatus.Cancelled, cancellationToken);
            var fromStatus = asset.Status;
            if (!closed) asset.Status = AssetStatus.Maintenance;
            // Closing work does not certify safety. Release is a separate authorized inspection.
            else if (!otherOpenJobs && job.ReleasedAt == null && asset.Status == AssetStatus.Maintenance) asset.Status = AssetStatus.Inspection;

            if (request.Status == MaintenanceStatus.Completed)
            {
                // Historical notes must not replace a schedule established by later work or an asset edit.
                if ((completing && (request.NextServiceDate.HasValue || request.IsPreventive)) || (scheduleChanged && asset.NextServiceDate == previousServiceDate
                    && !await db.MaintenanceJobs.AnyAsync(item => item.AssetId == job.AssetId && item.Id != job.Id
                        && item.Status == MaintenanceStatus.Completed && item.CompletedAt >= job.CompletedAt, cancellationToken)))
                    asset.NextServiceDate = request.NextServiceDate;
                if (request.MeterReading.HasValue && (completing || meterChanged))
                {
                    asset.CurrentMeterReading = request.MeterReading;
                    db.AssetMeterReadings.Add(new AssetMeterReading { AssetId = job.AssetId,
                        Type = asset.MeterUnit?.Contains("hour", StringComparison.OrdinalIgnoreCase) == true ? MeterType.EngineHours : MeterType.Odometer,
                        Unit = asset.MeterUnit ?? "unit", Reading = request.MeterReading.Value,
                        Source = MeterReadingSource.Maintenance, RecordedByUserId = scope.UserId });
                }
            }
            if (fromStatus != asset.Status)
                db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = job.AssetId,
                    Type = closed ? AssetLifecycleEventType.MaintenanceWorkClosed : AssetLifecycleEventType.MaintenanceStarted,
                    FromStatus = fromStatus, ToStatus = asset.Status, MeterReading = request.MeterReading,
                    Notes = $"Maintenance {job.JobNumber}: {request.Status}", RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        }
        AuditWriter.Record(db, scope, "Maintenance updated", "MaintenanceJob", job.Id,
            $"{job.JobNumber} was updated. {request.TransitionReason}", job.BranchId, previous,
            $"Status: {job.Status}; Actual cost: {job.ActualCost:0.00}");
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ActionResult?> ValidateReferences(Guid assetId, Guid? jobId, Guid? supplierId, Guid? parentId, CancellationToken token)
    {
        if (supplierId.HasValue && !await db.Suppliers.AnyAsync(item => item.Id == supplierId, token))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["supplierId"] = ["Select a valid supplier."] }));
        if (parentId.HasValue)
        {
            // A repeat failure must reference an earlier job on this same asset, never itself or a cycle.
            var visited = new HashSet<Guid>();
            if (jobId.HasValue) visited.Add(jobId.Value);
            var current = parentId;
            while (current.HasValue)
            {
                if (!visited.Add(current.Value))
                    return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["parentFailureJobId"] = ["Previous failure links cannot form a cycle."] }));
                var parent = await db.MaintenanceJobs.AsNoTracking().FirstOrDefaultAsync(item => item.Id == current && item.AssetId == assetId, token);
                if (parent is null)
                    return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["parentFailureJobId"] = ["Select a valid previous job for this asset."] }));
                current = parent.ParentFailureJobId;
            }
        }
        return null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SaveMaintenanceJobRequest(Guid AssetId, [Required] string ServiceType,
    [Required] string FaultDescription, string? AssignedTo, string? Supplier,
    [Range(0, 1000000)] decimal EstimatedCost, DateOnly? NextServiceDate, Guid? SupplierId = null, bool IsPreventive = false, [Range(0, 100000000)] decimal? NextServiceMeter = null, bool WarrantyCovered = false, string? WarrantyClaimNumber = null, Guid? ParentFailureJobId = null, [EnumDataType(typeof(MaintenancePriority))] MaintenancePriority Priority = MaintenancePriority.Normal,
    Guid? AssignedPersonnelId = null, [StringLength(30)] string SourceType = "Manual", Guid? SourceInspectionId = null, [StringLength(200)] string? SourceReference = null, DateTimeOffset? ExpectedReleaseAt = null, [Range(1,120)] int? ServiceIntervalMonths = null, bool? HasEstimate = null);
public sealed record UpdateMaintenanceJobRequest([EnumDataType(typeof(MaintenanceStatus))] MaintenanceStatus Status, [Required] string ServiceType,
    [Required] string FaultDescription, string? AssignedTo, string? Supplier,
    [Range(0, 1000000)] decimal EstimatedCost, [Range(0, 100000000)] decimal? ActualCost, string? PartsUsed, DateOnly? NextServiceDate,
    [Range(0, 100000000)] decimal PartsCost = 0, [Range(0, 100000000)] decimal LabourCost = 0,
    [Range(0, 100000000)] decimal TransportCost = 0, [Range(0, 100000000)] decimal ExternalServiceCost = 0,
    [Range(0, 100000000)] decimal TaxCost = 0, [Range(0, 100000000)] decimal OtherCost = 0,
    string? InvoiceNumber = null, [Range(0, 100000000)] decimal? MeterReading = null, [Range(0, 100000)] int DowntimeHours = 0,
    Guid? SupplierId = null, bool IsPreventive = false, [Range(0, 100000000)] decimal? NextServiceMeter = null, bool WarrantyCovered = false, string? WarrantyClaimNumber = null, Guid? ParentFailureJobId = null, [EnumDataType(typeof(MaintenancePriority))] MaintenancePriority Priority = MaintenancePriority.Normal, [StringLength(4000)] string? CompletionNotes = null, bool? UseDetailedCosts = null, DateTimeOffset? ExpectedVersion = null,
    [StringLength(1000)] string? TransitionReason = null, Guid? AssignedPersonnelId = null, DateTimeOffset? ExpectedReleaseAt = null,
    [Range(0,100000)] decimal? LabourHours = null, [Range(0,1000000)] decimal? LabourRate = null, [Range(0,100000000)] decimal FuelCost = 0, [Range(1,120)] int? ServiceIntervalMonths = null, bool? HasEstimate = null, bool CalculateDowntime = false);
