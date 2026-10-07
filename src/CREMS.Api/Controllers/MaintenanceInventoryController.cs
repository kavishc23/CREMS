using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController, Route("api/maintenance-jobs/{jobId:guid}/parts"), Authorize(Policy = SystemPolicies.ManageMaintenance)]
public sealed class MaintenanceInventoryController(ApplicationDbContext db, CurrentStaffScope staffScope,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(Guid jobId, CancellationToken token)
    {
        var job = await db.MaintenanceJobs.Include(x => x.Asset).FirstOrDefaultAsync(x => x.Id == jobId, token);
        if (job is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(job.BranchId, job.Asset?.DivisionId)) return Forbid();
        var canManage = (await authorization.AuthorizeAsync(User, SystemPolicies.ManageBranch)).Succeeded;
        var financial = (await authorization.AuthorizeAsync(User, SystemPermissions.AssetsViewFinancials)).Succeeded;
        var stock = await db.InventoryParts.AsNoTracking().Where(x => x.BranchId == job.BranchId)
            .Select(x => new { x.Id, x.PartNumber, x.Name, Available = x.QuantityOnHand - x.QuantityAllocated, x.UnitCost }).ToListAsync(token);
        var ledger = await db.MaintenancePartUsages.AsNoTracking().Where(x => x.MaintenanceJobId == jobId)
            .OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.InventoryPartId, x.InventoryPart!.PartNumber,
                x.Quantity, x.UnitCost, x.CreatedAt }).ToListAsync(token);
        return Ok(new { stock = stock.Select(x => { var row = new Dictionary<string, object?> { ["id"] = x.Id, ["partNumber"] = x.PartNumber, ["name"] = x.Name, ["available"] = x.Available }; if (financial) row["unitCost"] = x.UnitCost; return row; }),
            ledger = ledger.Select(x => { var row = new Dictionary<string, object?> { ["id"] = x.Id, ["inventoryPartId"] = x.InventoryPartId, ["partNumber"] = x.PartNumber, ["quantity"] = x.Quantity, ["createdAt"] = x.CreatedAt, ["batchKey"] = ledger.First(y => y.InventoryPartId == x.InventoryPartId && y.UnitCost == x.UnitCost).Id.ToString() }; if (financial) row["unitCost"] = x.UnitCost; return row; }),
            canManage, canFinancial = financial, canIssue = canManage && job.Status is not (MaintenanceStatus.Completed or MaintenanceStatus.Cancelled) });
    }

    [HttpPost("{usageId:guid}/return"), Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> Return(Guid jobId, Guid usageId, ReturnMaintenancePartRequest request, CancellationToken token)
    {
        if (request.Quantity < 1 || request.Quantity > 100000 || request.Quantity != decimal.Truncate(request.Quantity)
            || string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { message = "Enter a whole quantity and a reason for returning unused parts." });
        await using var transaction = await MaintenanceRules.BeginAsync(db, token);
        var job = await db.MaintenanceJobs.Include(x => x.Asset).FirstOrDefaultAsync(x => x.Id == jobId, token);
        var usage = await db.MaintenancePartUsages.Include(x => x.InventoryPart)
            .FirstOrDefaultAsync(x => x.Id == usageId && x.MaintenanceJobId == jobId && x.Quantity > 0, token);
        if (job is null || usage?.InventoryPart is null) return NotFound();
        var part = usage.InventoryPart;
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(job.BranchId, job.Asset?.DivisionId) || !scope.HasBranchAccess(part.BranchId)) return Forbid();
        if (job.Status == MaintenanceStatus.Completed && !(await authorization.AuthorizeAsync(User, SystemPermissions.MaintenanceComplete)).Succeeded) return Forbid();
        // Issues at the same original unit cost form a returnable balance; negative rows preserve the full audit trail.
        var outstanding = await db.MaintenancePartUsages.Where(x => x.MaintenanceJobId == jobId
            && x.InventoryPartId == part.Id && x.UnitCost == usage.UnitCost).SumAsync(x => x.Quantity, token);
        if (request.Quantity > outstanding) return BadRequest(new { message = "The return exceeds the outstanding issued quantity at this unit cost." });
        var cost = request.Quantity * usage.UnitCost;
        if (job.PartsCost < cost) return Conflict(new { message = "Job parts costs need reconciliation before this return can be recorded." });
        part.QuantityOnHand = checked(part.QuantityOnHand + (int)request.Quantity);
        var breakdown = MaintenanceCosts.Total(job);
        job.OtherCost += Math.Max(0, (job.ActualCost ?? breakdown) - breakdown);
        job.PartsCost -= cost;
        job.ActualCost = MaintenanceCosts.Total(job);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        db.MaintenancePartUsages.Add(new MaintenancePartUsage { MaintenanceJobId = jobId, InventoryPartId = part.Id,
            Quantity = -request.Quantity, UnitCost = usage.UnitCost });
        AuditWriter.Record(db, scope, "Maintenance parts returned", nameof(MaintenanceJob), job.Id,
            $"{request.Quantity} units of {part.PartNumber} returned at {usage.UnitCost}; issue {usageId}. Reason: {request.Reason.Trim()}", job.BranchId);
        await db.SaveChangesAsync(token);
        if (transaction is not null) await transaction.CommitAsync(token);
        return NoContent();
    }
}

public sealed record ReturnMaintenancePartRequest(decimal Quantity, [Required, StringLength(1000)] string Reason);
