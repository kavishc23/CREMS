using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/maintenance-jobs")]
[Authorize(Policy = SystemPolicies.ManageMaintenance)]
public sealed class MaintenanceJobsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> GetAll(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && scope.BranchIds.Count == 0)) return Forbid();
        var query = db.MaintenanceJobs.AsNoTracking();
        if (!scope.IsAdministrator) query = query.Where(job => scope.BranchIds.Contains(job.BranchId) && job.Asset!.DivisionId.HasValue && scope.DivisionIds.Contains(job.Asset.DivisionId.Value));
        return Ok(await query.OrderByDescending(job => job.ReportedAt).Select(job => new {
            job.Id, job.JobNumber, job.SupplierId, job.AssetId, AssetNumber = job.Asset!.AssetNumber, AssetName = job.Asset.Name,
            job.BranchId, BranchName = job.Branch!.Name, job.Status, job.Priority, CompletionNotes = job.Description, job.ServiceType, job.FaultDescription,
            job.AssignedTo, job.Supplier, job.EstimatedCost, job.ActualCost, job.PartsCost, job.LabourCost, job.TransportCost, job.ExternalServiceCost, job.TaxCost, job.OtherCost,
            job.PartsUsed, job.InvoiceNumber, job.MeterReading, job.DowntimeHours, job.IsPreventive, job.NextServiceMeter, job.WarrantyCovered, job.WarrantyClaimNumber, job.ParentFailureJobId, job.ReportedAt,
            job.CompletedAt, job.NextServiceDate }).ToListAsync(cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult> Create(SaveMaintenanceJobRequest request, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.Include(item => item.Branch).FirstOrDefaultAsync(item => item.Id == request.AssetId, cancellationToken);
        if (asset is null) return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["assetId"] = ["Select a valid asset."] }));
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        if (!asset.IsActive || asset.Status is not (AssetStatus.Available or AssetStatus.Maintenance))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["assetId"] = ["Only available assets or assets already in maintenance can enter maintenance. Resolve any hire, reservation or inspection first."] }));
        var job = new MaintenanceJob { JobNumber = $"MNT-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = request.ServiceType.Trim(),
            FaultDescription = request.FaultDescription.Trim(), AssignedTo = Normalize(request.AssignedTo),
            Supplier = Normalize(request.Supplier), SupplierId = request.SupplierId, EstimatedCost = request.EstimatedCost, IsPreventive = request.IsPreventive, NextServiceMeter = request.NextServiceMeter, WarrantyCovered = request.WarrantyCovered, WarrantyClaimNumber = Normalize(request.WarrantyClaimNumber), ParentFailureJobId = request.ParentFailureJobId,
            NextServiceDate = request.NextServiceDate, Priority = request.Priority, Status = MaintenanceStatus.Open };
        var previousAssetStatus = asset.Status;
        asset.Status = AssetStatus.Maintenance;
        db.MaintenanceJobs.Add(job);
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, Type = AssetLifecycleEventType.MaintenanceStarted, FromStatus = previousAssetStatus, ToStatus = AssetStatus.Maintenance, Notes = job.FaultDescription, RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        AuditWriter.Record(db, scope, "Maintenance logged", "MaintenanceJob", job.Id,
            $"{job.JobNumber} opened for {asset.AssetNumber}.", asset.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { job.Id, job.JobNumber });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, UpdateMaintenanceJobRequest request, CancellationToken cancellationToken)
    {
        var job = await db.MaintenanceJobs.Include(item => item.Asset).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (job is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(job.BranchId, job.Asset?.DivisionId)) return Forbid();
        if (request.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
            && (job.Asset is null || !job.Asset.IsActive || job.Asset.Status is not (AssetStatus.Available or AssetStatus.Maintenance)))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["This asset cannot enter maintenance in its current state."] }));
        if (request.Status == MaintenanceStatus.Completed && request.MeterReading.HasValue
            && job.Asset?.CurrentMeterReading > request.MeterReading)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["meterReading"] = ["The meter reading cannot be lower than the asset's current reading."] }));
        var previousStatus = job.Status;
        var previous = $"Status: {job.Status}; Actual cost: {job.ActualCost:0.00}";
        job.Priority = request.Priority; job.Description = Normalize(request.CompletionNotes);
        job.Status = request.Status; job.ServiceType = request.ServiceType.Trim();
        job.FaultDescription = request.FaultDescription.Trim(); job.AssignedTo = Normalize(request.AssignedTo);
        job.Supplier = Normalize(request.Supplier); job.EstimatedCost = request.EstimatedCost;
        job.SupplierId = request.SupplierId; job.IsPreventive = request.IsPreventive; job.NextServiceMeter = request.NextServiceMeter; job.WarrantyCovered = request.WarrantyCovered; job.WarrantyClaimNumber = Normalize(request.WarrantyClaimNumber); job.ParentFailureJobId = request.ParentFailureJobId;
        job.PartsCost = request.PartsCost; job.LabourCost = request.LabourCost; job.TransportCost = request.TransportCost;
        job.ExternalServiceCost = request.ExternalServiceCost; job.TaxCost = request.TaxCost; job.OtherCost = request.OtherCost;
        var detailedTotal = request.PartsCost + request.LabourCost + request.TransportCost + request.ExternalServiceCost + request.TaxCost + request.OtherCost;
        job.ActualCost = detailedTotal > 0 ? detailedTotal : request.ActualCost;
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
            else if (!otherOpenJobs && asset.Status == AssetStatus.Maintenance) asset.Status = AssetStatus.Available;

            if (request.Status == MaintenanceStatus.Completed)
            {
                asset.NextServiceDate = request.NextServiceDate;
                if (request.MeterReading.HasValue && (previousStatus != MaintenanceStatus.Completed || asset.CurrentMeterReading != request.MeterReading))
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
                    Type = closed ? AssetLifecycleEventType.ReturnedToService : AssetLifecycleEventType.MaintenanceStarted,
                    FromStatus = fromStatus, ToStatus = asset.Status, MeterReading = request.MeterReading,
                    Notes = $"Maintenance {job.JobNumber}: {request.Status}", RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        }
        AuditWriter.Record(db, scope, "Maintenance updated", "MaintenanceJob", job.Id,
            $"{job.JobNumber} was updated.", job.BranchId, previous,
            $"Status: {job.Status}; Actual cost: {job.ActualCost:0.00}");
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SaveMaintenanceJobRequest(Guid AssetId, [Required] string ServiceType,
    [Required] string FaultDescription, string? AssignedTo, string? Supplier,
    [Range(0, 1000000)] decimal EstimatedCost, DateOnly? NextServiceDate, Guid? SupplierId = null, bool IsPreventive = false, [Range(0, 100000000)] decimal? NextServiceMeter = null, bool WarrantyCovered = false, string? WarrantyClaimNumber = null, Guid? ParentFailureJobId = null, [EnumDataType(typeof(MaintenancePriority))] MaintenancePriority Priority = MaintenancePriority.Normal);
public sealed record UpdateMaintenanceJobRequest([EnumDataType(typeof(MaintenanceStatus))] MaintenanceStatus Status, [Required] string ServiceType,
    [Required] string FaultDescription, string? AssignedTo, string? Supplier,
    [Range(0, 1000000)] decimal EstimatedCost, [Range(0, 100000000)] decimal? ActualCost, string? PartsUsed, DateOnly? NextServiceDate,
    [Range(0, 100000000)] decimal PartsCost = 0, [Range(0, 100000000)] decimal LabourCost = 0,
    [Range(0, 100000000)] decimal TransportCost = 0, [Range(0, 100000000)] decimal ExternalServiceCost = 0,
    [Range(0, 100000000)] decimal TaxCost = 0, [Range(0, 100000000)] decimal OtherCost = 0,
    string? InvoiceNumber = null, [Range(0, 100000000)] decimal? MeterReading = null, [Range(0, 100000)] int DowntimeHours = 0,
    Guid? SupplierId = null, bool IsPreventive = false, [Range(0, 100000000)] decimal? NextServiceMeter = null, bool WarrantyCovered = false, string? WarrantyClaimNumber = null, Guid? ParentFailureJobId = null, [EnumDataType(typeof(MaintenancePriority))] MaintenancePriority Priority = MaintenancePriority.Normal, [StringLength(4000)] string? CompletionNotes = null);
