using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/maintenance-jobs")]
[Authorize(Policy = SystemPolicies.ManageBranch)]
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
            job.AssignedTo, job.Supplier, job.EstimatedCost, job.ActualCost, job.PartsUsed, job.ReportedAt,
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
            Supplier = Normalize(request.Supplier), EstimatedCost = request.EstimatedCost,
            NextServiceDate = request.NextServiceDate, Status = MaintenanceStatus.Open };
        asset.Status = AssetStatus.Maintenance;
        db.MaintenanceJobs.Add(job);
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
        job.ActualCost = request.ActualCost; job.PartsUsed = Normalize(request.PartsUsed);
        job.NextServiceDate = request.NextServiceDate; job.UpdatedAt = DateTimeOffset.UtcNow;
        if (request.Status == MaintenanceStatus.Completed)
        {
            job.CompletedAt ??= DateTimeOffset.UtcNow;
            if (job.Asset is not null) { job.Asset.Status = AssetStatus.Available; job.Asset.NextServiceDate = request.NextServiceDate; }
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
    [Range(0, 1000000)] decimal EstimatedCost, DateOnly? NextServiceDate);
public sealed record UpdateMaintenanceJobRequest(MaintenanceStatus Status, [Required] string ServiceType,
    [Required] string FaultDescription, string? AssignedTo, string? Supplier,
    decimal EstimatedCost, decimal? ActualCost, string? PartsUsed, DateOnly? NextServiceDate);
