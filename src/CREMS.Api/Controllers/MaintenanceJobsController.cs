using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
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
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var query = db.MaintenanceJobs.AsNoTracking();
        if (!scope.IsAdministrator) query = query.Where(job => job.BranchId == scope.BranchId && job.Asset!.DivisionId == scope.DivisionId);
        return Ok(await query.OrderByDescending(job => job.ReportedAt).Select(job => new {
            job.Id, job.JobNumber, job.AssetId, AssetNumber = job.Asset!.AssetNumber, AssetName = job.Asset.Name,
            job.BranchId, BranchName = job.Branch!.Name, job.Status, job.ServiceType, job.FaultDescription,
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
        var job = new MaintenanceJob { JobNumber = $"MNT-{DateTime.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = request.ServiceType.Trim(),
            FaultDescription = request.FaultDescription.Trim(), AssignedTo = Normalize(request.AssignedTo),
            Supplier = Normalize(request.Supplier), SupplierId = request.SupplierId, EstimatedCost = request.EstimatedCost, IsPreventive = request.IsPreventive, NextServiceMeter = request.NextServiceMeter, WarrantyCovered = request.WarrantyCovered, WarrantyClaimNumber = Normalize(request.WarrantyClaimNumber), ParentFailureJobId = request.ParentFailureJobId,
            NextServiceDate = request.NextServiceDate, Status = MaintenanceStatus.Open };
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
        var previous = $"Status: {job.Status}; Actual cost: {job.ActualCost:0.00}";
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
        if (request.Status == MaintenanceStatus.Completed)
        {
            job.CompletedAt ??= DateTimeOffset.UtcNow;
            if (job.Asset is not null) { var fromStatus = job.Asset.Status; job.Asset.Status = AssetStatus.Available; job.Asset.NextServiceDate = request.NextServiceDate; if (request.MeterReading.HasValue) { job.Asset.CurrentMeterReading = request.MeterReading; db.AssetMeterReadings.Add(new AssetMeterReading { AssetId = job.AssetId, Type = job.Asset.MeterUnit?.Contains("hour", StringComparison.OrdinalIgnoreCase) == true ? MeterType.EngineHours : MeterType.Odometer, Unit = job.Asset.MeterUnit ?? "unit", Reading = request.MeterReading.Value, Source = MeterReadingSource.Maintenance, RecordedByUserId = scope.UserId }); } db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = job.AssetId, Type = AssetLifecycleEventType.ReturnedToService, FromStatus = fromStatus, ToStatus = AssetStatus.Available, MeterReading = request.MeterReading, Notes = $"Maintenance {job.JobNumber} completed", RecordedByUserId = scope.UserId, RecordedByName = scope.UserName }); }
        }
        else if (job.Asset is not null) job.Asset.Status = AssetStatus.Maintenance;
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
    [Range(0, 1000000)] decimal EstimatedCost, DateOnly? NextServiceDate, Guid? SupplierId = null, bool IsPreventive = false, decimal? NextServiceMeter = null, bool WarrantyCovered = false, string? WarrantyClaimNumber = null, Guid? ParentFailureJobId = null);
public sealed record UpdateMaintenanceJobRequest(MaintenanceStatus Status, [Required] string ServiceType,
    [Required] string FaultDescription, string? AssignedTo, string? Supplier,
    decimal EstimatedCost, decimal? ActualCost, string? PartsUsed, DateOnly? NextServiceDate,
    [Range(0, 100000000)] decimal PartsCost = 0, [Range(0, 100000000)] decimal LabourCost = 0,
    [Range(0, 100000000)] decimal TransportCost = 0, [Range(0, 100000000)] decimal ExternalServiceCost = 0,
    [Range(0, 100000000)] decimal TaxCost = 0, [Range(0, 100000000)] decimal OtherCost = 0,
    string? InvoiceNumber = null, [Range(0, 100000000)] decimal? MeterReading = null, [Range(0, 100000)] int DowntimeHours = 0,
    Guid? SupplierId = null, bool IsPreventive = false, decimal? NextServiceMeter = null, bool WarrantyCovered = false, string? WarrantyClaimNumber = null, Guid? ParentFailureJobId = null);
