using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController, Route("api/maintenance-jobs"), Authorize(Policy = SystemPolicies.ManageMaintenance)]
public sealed class MaintenanceWorkspaceController(ApplicationDbContext db, CurrentStaffScope scopes, IAuthorizationService authorization) : ControllerBase
{
    private Task<StaffDataScope?> Scope() => scopes.GetAsync(User);
    private async Task<bool> Allowed(string permission) => (await authorization.AuthorizeAsync(User, permission)).Succeeded;
    private IQueryable<MaintenanceJob> Jobs(StaffDataScope scope) => db.MaintenanceJobs.AsNoTracking()
        .Where(x => scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId) && x.Asset!.DivisionId.HasValue && scope.DivisionIds.Contains(x.Asset.DivisionId.Value));
    private IQueryable<Asset> Assets(StaffDataScope scope) => db.Assets.AsNoTracking()
        .Where(x => scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId) && x.DivisionId.HasValue && scope.DivisionIds.Contains(x.DivisionId.Value));

    [HttpGet("workspace")]
    public async Task<ActionResult> List([FromQuery] string queue = "Active", [FromQuery] string? search = null,
        [FromQuery] Guid? assetId = null, [FromQuery] Guid? branchId = null, [FromQuery] string? technician = null,
        [FromQuery] MaintenanceStatus? status = null, [FromQuery] MaintenancePriority? priority = null,
        [FromQuery] bool unassigned = false, [FromQuery] bool overdueRepairs = false, [FromQuery] string sort = "reported", [FromQuery] bool ascending = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken token = default)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        if (page < 1 || page > 100000 || pageSize is not (25 or 50 or 100)) return BadRequest(new { message = "Use a page size of 25, 50 or 100." });
        var financial = await Allowed(SystemPermissions.AssetsViewFinancials);
        var all = Jobs(scope); var query = all;
        query = queue switch {
            "Active" => query.Where(x => x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled),
            "Preventive" => query.Where(x => x.IsPreventive && x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled),
            "AwaitingRelease" => query.Where(x => (x.Status == MaintenanceStatus.Completed || x.Status == MaintenanceStatus.Cancelled) && x.ReleasedAt == null),
            "History" => query,
            _ => null!
        };
        if (query is null) return BadRequest(new { message = "Select a valid maintenance view." });
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.JobNumber.Contains(term) || x.Asset!.AssetNumber.Contains(term) || x.Asset.Name.Contains(term) || x.Branch!.Name.Contains(term) || x.FaultDescription.Contains(term) || x.ServiceType.Contains(term) || (x.AssignedTo != null && x.AssignedTo.Contains(term)) || (x.Supplier != null && x.Supplier.Contains(term))); }
        if (assetId.HasValue) query = query.Where(x => x.AssetId == assetId);
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId);
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (priority.HasValue) query = query.Where(x => x.Priority == priority);
        if (!string.IsNullOrWhiteSpace(technician)) query = query.Where(x => x.AssignedTo == technician);
        if (unassigned) query = query.Where(x => x.AssignedTo == null && x.Supplier == null);
        var now = DateTimeOffset.UtcNow;
        if (overdueRepairs) query = query.Where(x => x.ReleasedAt == null && x.Status != MaintenanceStatus.Cancelled && x.ExpectedReleaseAt < now);
        IOrderedQueryable<MaintenanceJob> ordered = sort switch {
            "asset" => ascending ? query.OrderBy(x => x.Asset!.AssetNumber) : query.OrderByDescending(x => x.Asset!.AssetNumber),
            "status" => ascending ? query.OrderBy(x => x.Status) : query.OrderByDescending(x => x.Status),
            "branch" => ascending ? query.OrderBy(x => x.Branch!.Name) : query.OrderByDescending(x => x.Branch!.Name),
            "reference" => ascending ? query.OrderBy(x => x.JobNumber) : query.OrderByDescending(x => x.JobNumber),
            "priority" => ascending ? query.OrderBy(x => x.Priority) : query.OrderByDescending(x => x.Priority),
            "release" => ascending ? query.OrderBy(x => x.ExpectedReleaseAt) : query.OrderByDescending(x => x.ExpectedReleaseAt),
            _ => ascending ? query.OrderBy(x => x.ReportedAt) : query.OrderByDescending(x => x.ReportedAt)
        };
        var total = await query.CountAsync(token);
        var items = await ordered.ThenBy(x => x.Id).Include(x => x.Asset).Include(x => x.Branch).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
        var summary = branchId.HasValue ? all.Where(x => x.BranchId == branchId) : all;
        var counts = new {
            overdueRepairs = await summary.CountAsync(x => x.ReleasedAt == null && x.Status != MaintenanceStatus.Cancelled && x.ExpectedReleaseAt < now, token),
            active = await summary.CountAsync(x => x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled, token),
            unassigned = await summary.CountAsync(x => x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled && x.AssignedTo == null && x.Supplier == null, token),
            inProgress = await summary.CountAsync(x => x.Status == MaintenanceStatus.InProgress, token),
            waiting = await summary.CountAsync(x => x.Status == MaintenanceStatus.WaitingForParts, token),
            awaitingRelease = await summary.CountAsync(x => (x.Status == MaintenanceStatus.Completed || x.Status == MaintenanceStatus.Cancelled) && x.ReleasedAt == null, token),
            preventive = await summary.CountAsync(x => x.IsPreventive && x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled, token)
        };
        return Ok(new { items = items.Select(x => MaintenanceWorkspace.Record(x, financial)), total, page, pageSize, counts,
            permissions = new { canComplete = await Allowed(SystemPermissions.MaintenanceComplete), canInspect = await Allowed(SystemPermissions.AssetsInspect), canFinancial = financial } });
    }

    [HttpGet("options")]
    public async Task<ActionResult> Options(CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        var branches = await Assets(scope).Select(x => new { x.BranchId, x.Branch!.Name }).Distinct().OrderBy(x => x.Name).ToListAsync(token);
        var technicians = await db.Personnel.AsNoTracking().Where(x => x.IsActive && x.Type == PersonnelType.Technician &&
            (scope.IsAdministrator || scope.BranchIds.Contains(x.BranchId) && scope.DivisionIds.Contains(x.DivisionId)))
            .OrderBy(x => x.FullName).Select(x => new { x.Id, x.FullName, x.EmployeeNumber, x.BranchId, x.DivisionId, x.Availability }).ToListAsync(token);
        var suppliers = await db.Suppliers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.SupplierNumber }).ToListAsync(token);
        var technicianNames = await Jobs(scope).Where(x => x.AssignedTo != null).Select(x => x.AssignedTo).Distinct().OrderBy(x => x).ToListAsync(token);
        return Ok(new { branches, technicians, suppliers, technicianNames });
    }

    [HttpGet("schedule")]
    public async Task<ActionResult> Schedule([FromQuery] string? search = null, [FromQuery] Guid? branchId = null,
        [FromQuery] bool dueOnly = false, [FromQuery] bool overdueOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken token = default)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        if (page < 1 || page > 100000 || pageSize is not (25 or 50 or 100)) return BadRequest();
        var today = MaintenanceRules.LocalDate(DateTimeOffset.UtcNow);
        var assets = Assets(scope).Where(x => x.IsActive && x.Status != AssetStatus.Retired);
        if (branchId.HasValue) assets = assets.Where(x => x.BranchId == branchId);
        // JSON intervals need managed parsing. Only target-bearing assets enter this scoped batch.
        assets = assets.Where(x => x.NextServiceDate != null || x.ServiceOfferingId != null || db.MaintenanceJobs.Any(j => j.AssetId == x.Id && j.Status == MaintenanceStatus.Completed && j.NextServiceMeter != null));
        var plans = await MaintenanceRules.PlansAsync(db, assets, today, token);
        var dueCount = plans.Count(x => x.IsDue); var overdueCount = plans.Count(x => x.IsOverdue);
        IEnumerable<ServicePlan> filtered = plans;
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); filtered = filtered.Where(x => x.AssetNumber.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || x.BranchName.Contains(term, StringComparison.OrdinalIgnoreCase)); }
        if (overdueOnly) filtered = filtered.Where(x => x.IsOverdue);
        else if (dueOnly) filtered = filtered.Where(x => x.IsDue);
        var sorted = filtered.OrderByDescending(x => x.IsOverdue).ThenByDescending(x => x.IsDue).ThenBy(x => x.NextServiceDate).ThenBy(x => x.AssetNumber).ToList();
        return Ok(new { items = sorted.Skip((page - 1) * pageSize).Take(pageSize), total = sorted.Count, page, pageSize, dueCount, overdueCount, today });
    }

    [HttpGet("inspections/{inspectionId:guid}")]
    public async Task<ActionResult> InspectionRecord(Guid inspectionId, CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        var record = await db.AssetInspections.AsNoTracking().Where(x => x.Id == inspectionId && Assets(scope).Any(a => a.Id == x.AssetId))
            .Select(x => new { x.Id, x.AssetId, AssetNumber = x.Asset!.AssetNumber, x.Stage, x.Outcome, x.CompletedAt, x.CompletedByName,
                x.Notes, x.MeterReading, MeterUnit = x.Asset!.MeterUnit, x.FuelPercent, x.ResponsesJson }).SingleOrDefaultAsync(token);
        return record == null ? NotFound() : Ok(record);
    }

    [HttpGet("assets")]
    public async Task<ActionResult> AssetOptions([FromQuery] string? search = null, [FromQuery] Guid? id = null, CancellationToken token = default)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        var query = Assets(scope);
        if (id.HasValue) query = query.Where(x => x.Id == id);
        else { query = query.Where(x => x.IsActive && (x.Status == AssetStatus.Available || x.Status == AssetStatus.Maintenance || x.Status == AssetStatus.OutOfService));
            if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.AssetNumber.Contains(term) || x.Name.Contains(term) || (x.RegistrationNumber != null && x.RegistrationNumber.Contains(term))); } }
        return Ok(await query.OrderBy(x => x.AssetNumber).Take(40).Select(x => new { x.Id, x.AssetNumber, x.Name, x.BranchId, x.DivisionId, BranchName = x.Branch!.Name, x.Status, x.MeterUnit, x.CurrentMeterReading, x.NextServiceDate }).ToListAsync(token));
    }

    [HttpGet("inspections")]
    public async Task<ActionResult> InspectionOptions([FromQuery] Guid assetId, CancellationToken token)
    {
        var scope = await Scope(); if (scope is null || !await Assets(scope).AnyAsync(x => x.Id == assetId, token)) return Forbid();
        return Ok(await db.AssetInspections.AsNoTracking().Where(x => x.AssetId == assetId).OrderByDescending(x => x.CompletedAt).Take(50)
            .Select(x => new { x.Id, x.Stage, x.Outcome, x.CompletedAt, x.Notes }).ToListAsync(token));
    }

    [HttpGet("{id:guid}/workspace")]
    public async Task<ActionResult> Detail(Guid id, CancellationToken token)
    {
        var scope = await Scope(); if (scope is null) return Forbid();
        var job = await Jobs(scope).Include(x => x.Asset).ThenInclude(x => x!.Division).Include(x => x.Branch).SingleOrDefaultAsync(x => x.Id == id, token);
        if (job?.Asset is null) return NotFound();
        var financial = await Allowed(SystemPermissions.AssetsViewFinancials);
        var template = await db.InspectionTemplates.AsNoTracking().Where(x => x.IsActive && x.AssetCategoryId == job.Asset.AssetCategoryId && x.Stage == InspectionStage.Maintenance).OrderBy(x => x.Id).FirstOrDefaultAsync(token);
        var history = await Jobs(scope).Where(x => x.AssetId == job.AssetId).OrderByDescending(x => x.ReportedAt).Take(50).Include(x => x.Asset).Include(x => x.Branch).ToListAsync(token);
        // Query blockers independently of the capped history so older open repairs remain visible.
        var blockingJobs = await Jobs(scope).Where(x => x.AssetId == job.AssetId && x.Id != id && x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled)
            .OrderBy(x => x.ReportedAt).ThenBy(x => x.Id).Include(x => x.Asset).Include(x => x.Branch).ToListAsync(token);
        var blockingJobCount = await db.MaintenanceJobs.CountAsync(x => x.AssetId == job.AssetId && x.Id != id && x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled, token);
        var audits = await db.AuditEvents.AsNoTracking().Where(x => x.EntityType == nameof(MaintenanceJob) && x.EntityId == id).OrderByDescending(x => x.OccurredAt).Take(50)
            .Select(x => new { x.Id, x.Action, x.UserName, x.OccurredAt, Summary = financial ? x.Summary : x.Action }).ToListAsync(token);
        var meters = await db.AssetMeterReadings.AsNoTracking().Where(x => x.AssetId == job.AssetId).OrderByDescending(x => x.RecordedAt).Take(20).Select(x => new { x.Reading, x.Unit, x.RecordedAt, x.Source }).ToListAsync(token);
        var inspections = await db.AssetInspections.AsNoTracking().Where(x => x.AssetId == job.AssetId).OrderByDescending(x => x.CompletedAt).Take(20).Select(x => new { x.Id, x.Stage, x.Outcome, x.CompletedAt, x.CompletedByName, x.Notes }).ToListAsync(token);
        var closed = Jobs(scope).Where(x => x.AssetId == job.AssetId && x.Status == MaintenanceStatus.Completed);
        var metrics = new Dictionary<string, object?> { ["completedJobs"] = await closed.CountAsync(token), ["downtimeHours"] = await closed.SumAsync(x => (int?)x.DowntimeHours, token) ?? 0,
            ["repeatFailures"] = await Jobs(scope).CountAsync(x => x.AssetId == job.AssetId && x.ParentFailureJobId != null, token) };
        if (financial) { metrics["totalRecordedCost"] = await Jobs(scope).Where(x => x.AssetId == job.AssetId).SumAsync(x => x.ActualCost ?? 0, token); var total = await closed.SumAsync(x => x.ActualCost ?? 0, token); metrics["totalCost"] = total; metrics["bookValue"] = job.Asset.CurrentBookValue; metrics["costToBookValuePercent"] = job.Asset.CurrentBookValue > 0 ? decimal.Round(total / job.Asset.CurrentBookValue.Value * 100, 1) : null; }
        var servicePlan = (await MaintenanceRules.PlansAsync(db, Assets(scope).Where(x => x.Id == job.AssetId), MaintenanceRules.LocalDate(DateTimeOffset.UtcNow), token)).Single();
        var issuedCost = financial ? await db.MaintenancePartUsages.Where(x => x.MaintenanceJobId == id).SumAsync(x => (decimal?)(x.Quantity * x.UnitCost), token) ?? 0 : 0;
        return Ok(new { blockingJobs = blockingJobs.Select(x => MaintenanceWorkspace.Record(x, financial)), restrictedBlockingJobCount = blockingJobCount - blockingJobs.Count, servicePlan, job = MaintenanceWorkspace.Record(job, financial, issuedCost), history = history.Select(x => MaintenanceWorkspace.Record(x, financial)), audits, meters, inspections, metrics,
            safetyChecks = MaintenanceWorkspace.Checks(job.Asset, template?.ChecklistJson), templateId = template?.Id,
            permissions = new { canComplete = await Allowed(SystemPermissions.MaintenanceComplete), canInspect = await Allowed(SystemPermissions.AssetsInspect), canFinancial = financial } });
    }

    [HttpPost("{id:guid}/release")]
    public async Task<ActionResult> Release(Guid id, ReleaseMaintenanceRequest request, CancellationToken token)
    {
        if (!await Allowed(SystemPermissions.MaintenanceComplete) || !await Allowed(SystemPermissions.AssetsInspect)) return Forbid();
        await using var transaction = await MaintenanceRules.BeginAsync(db, token);
        var scope = await Scope(); var job = await db.MaintenanceJobs.Include(x => x.Asset).SingleOrDefaultAsync(x => x.Id == id, token);
        if (job?.Asset is null) return NotFound();
        if (scope is null || !scope.HasAssetAccess(job.BranchId, job.Asset.DivisionId)) return Forbid();
        if (request.ExpectedVersion != (job.UpdatedAt ?? job.CreatedAt)) return Conflict(new { message = "This job changed. Refresh before returning the asset to service." });
        if (job.ReleasedAt.HasValue) return Conflict(new { message = "This job has already been released." });
        if (job.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)) return BadRequest(new { message = "Complete or cancel the work before the safety check." });
        var asset = job.Asset; var now = DateTimeOffset.UtcNow;
        if (!asset.IsActive || asset.BranchId != job.BranchId || asset.Status is not (AssetStatus.Maintenance or AssetStatus.Inspection or AssetStatus.OutOfService)) return BadRequest(new { message = "Resolve the asset's current branch and operating status first." });
        if (await db.MaintenanceJobs.AnyAsync(x => x.AssetId == asset.Id && x.Id != id && x.Status != MaintenanceStatus.Completed && x.Status != MaintenanceStatus.Cancelled, token))
            return BadRequest(new { message = "Other maintenance work is still open for this asset." });
        if (await db.BookingItems.AnyAsync(x => x.AssetId == asset.Id && (x.Booking!.Status == BookingStatus.ConvertedToRental || x.Booking.Status == BookingStatus.Confirmed && x.StartAt <= now && x.EndAt > now), token)
            || await db.AssetTransfers.AnyAsync(x => x.AssetId == asset.Id && (x.Status == TransferStatus.Approved || x.Status == TransferStatus.InTransit || x.Status == TransferStatus.Received), token))
            return BadRequest(new { message = "An active hire, allocation or transfer blocks release." });
        if (string.IsNullOrWhiteSpace(request.Notes)) return BadRequest(new { message = "Record the safety check result and test run notes." });
        if (!string.IsNullOrWhiteSpace(asset.MeterUnit) && !request.MeterReading.HasValue) return BadRequest(new { message = "Record the final meter reading." });
        if (request.MeterReading < asset.CurrentMeterReading) return BadRequest(new { message = "The meter reading cannot decrease." });
        if (asset.NextServiceDate <= MaintenanceRules.LocalDate(now)) return BadRequest(new { message = "Set a future service date before release; the asset is still due for service." });
        var plan = (await MaintenanceRules.PlansAsync(db, Assets(scope).Where(x => x.Id == asset.Id), MaintenanceRules.LocalDate(now), token)).Single();
        if (plan.NextServiceMeter.HasValue && plan.NextServiceMeter <= (request.MeterReading ?? asset.CurrentMeterReading)) return BadRequest(new { message = "Set a future service meter threshold before release; the asset is still due for service." });
        var template = await db.InspectionTemplates.Where(x => x.IsActive && x.AssetCategoryId == asset.AssetCategoryId && x.Stage == InspectionStage.Maintenance).OrderBy(x => x.Id).FirstOrDefaultAsync(token);
        var checks = MaintenanceWorkspace.Checks(asset, template?.ChecklistJson);
        if (request.PassedChecks is null || checks.Any(x => !request.PassedChecks.Contains(x))) return BadRequest(new { message = "Every applicable safety check must pass. Keep the asset unavailable if any check fails." });
        var latestFailure = await db.AssetInspections.Where(x => x.AssetId == asset.Id && (x.Outcome == InspectionOutcome.Failed || x.Outcome == InspectionOutcome.DamageDetected)).OrderByDescending(x => x.CompletedAt).FirstOrDefaultAsync(token);
        if (latestFailure != null && latestFailure.CompletedAt > (job.CompletedAt ?? job.UpdatedAt ?? job.ReportedAt)) return BadRequest(new { message = "A newer failed inspection requires further maintenance before release." });
        var inspection = new AssetInspection { AssetId = asset.Id, TemplateId = template?.Id, Stage = InspectionStage.Maintenance, Outcome = InspectionOutcome.Passed,
            ResponsesJson = JsonSerializer.Serialize(checks.Select(x => new { label = x, passed = true })), Notes = request.Notes.Trim(), MeterReading = request.MeterReading,
            StaffSignatureName = scope.UserName, CompletedByUserId = scope.UserId, CompletedByName = scope.UserName, CompletedAt = now };
        db.AssetInspections.Add(inspection);
        var closedJobs = await db.MaintenanceJobs.Where(x => x.AssetId == asset.Id && x.ReleasedAt == null && (x.Status == MaintenanceStatus.Completed || x.Status == MaintenanceStatus.Cancelled)).ToListAsync(token);
        foreach (var closedJob in closedJobs) { closedJob.ReleasedAt = now; closedJob.ReleasedByName = scope.UserName; closedJob.ReleaseInspectionId = inspection.Id; closedJob.UpdatedAt = now;
            AuditWriter.Record(db, scope, "Asset returned to service", nameof(MaintenanceJob), closedJob.Id, $"Safety check passed. {request.Notes.Trim()}", closedJob.BranchId); }
        var previous = asset.Status; asset.Status = AssetStatus.Available;
        if (request.MeterReading.HasValue) { asset.CurrentMeterReading = request.MeterReading; db.AssetMeterReadings.Add(new AssetMeterReading { AssetId = asset.Id, Unit = asset.MeterUnit ?? "unit", Type = asset.MeterUnit?.Contains("hour", StringComparison.OrdinalIgnoreCase) == true ? MeterType.EngineHours : MeterType.Odometer, Reading = request.MeterReading.Value, Source = MeterReadingSource.Maintenance, RecordedByUserId = scope.UserId }); }
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, Type = AssetLifecycleEventType.ReturnedToService, FromStatus = previous, ToStatus = asset.Status, Notes = request.Notes, RecordedByUserId = scope.UserId, RecordedByName = scope.UserName });
        await db.SaveChangesAsync(token); if (transaction is not null) await transaction.CommitAsync(token);
        return Ok(new { message = $"{asset.AssetNumber} passed its safety check and is available.", inspectionId = inspection.Id });
    }
}

public sealed record ReleaseMaintenanceRequest(DateTimeOffset ExpectedVersion, [Required, StringLength(4000)] string Notes, [Range(0,100000000)] decimal? MeterReading, string[] PassedChecks);
