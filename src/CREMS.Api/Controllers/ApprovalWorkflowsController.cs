using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/approval-workflows")]
[Authorize(Policy = SystemPolicies.AdministerSystem)]
public sealed class ApprovalWorkflowsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken token) => Ok(await db.ApprovalWorkflows.AsNoTracking().Include(x => x.Stages).OrderBy(x => x.Type).ThenBy(x => x.Name).ToListAsync(token));

    [HttpPost]
    public async Task<ActionResult> Create(SaveApprovalWorkflowRequest request, CancellationToken token)
    {
        if (request.Stages.Count is < 1 or > 5 || request.Stages.Select(x => x.Sequence).Distinct().Count() != request.Stages.Count)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["stages"] = ["Configure between one and five uniquely ordered approval phases."] }));
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        var workflow = new ApprovalWorkflow { Name = request.Name.Trim(), Type = request.Type, EntityType = Clean(request.EntityType), BranchId = request.BranchId, DivisionId = request.DivisionId, IsActive = request.IsActive,
            Stages = request.Stages.OrderBy(x => x.Sequence).Select(MapStage).ToList() };
        db.ApprovalWorkflows.Add(workflow); AuditWriter.Record(db, scope, "Approval workflow created", nameof(ApprovalWorkflow), workflow.Id, $"{workflow.Name} created with {workflow.Stages.Count} phases.", request.BranchId);
        await db.SaveChangesAsync(token); return Ok(workflow);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, SaveApprovalWorkflowRequest request, CancellationToken token)
    {
        var workflow = await db.ApprovalWorkflows.Include(x => x.Stages).FirstOrDefaultAsync(x => x.Id == id, token); if (workflow is null) return NotFound();
        if (request.Stages.Count is < 1 or > 5 || request.Stages.Select(x => x.Sequence).Distinct().Count() != request.Stages.Count) return BadRequest();
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        db.ApprovalWorkflowStages.RemoveRange(workflow.Stages); workflow.Name = request.Name.Trim(); workflow.Type = request.Type; workflow.EntityType = Clean(request.EntityType); workflow.BranchId = request.BranchId; workflow.DivisionId = request.DivisionId; workflow.IsActive = request.IsActive;
        workflow.Stages = request.Stages.OrderBy(x => x.Sequence).Select(MapStage).ToList(); workflow.UpdatedAt = DateTimeOffset.UtcNow;
        AuditWriter.Record(db, scope, "Approval workflow updated", nameof(ApprovalWorkflow), workflow.Id, $"{workflow.Name} now has {workflow.Stages.Count} phases.", request.BranchId); await db.SaveChangesAsync(token); return Ok(workflow);
    }
    [HttpGet("delegations")]
    public async Task<ActionResult> Delegations(CancellationToken token) => Ok(await db.ApprovalDelegations.AsNoTracking().OrderByDescending(x => x.StartsAt).ToListAsync(token));

    [HttpPost("delegations")]
    public async Task<ActionResult> Delegate(ApprovalDelegationRequest request, CancellationToken token)
    {
        if (request.FromUserId == request.ToUserId || request.EndsAt <= request.StartsAt) return BadRequest(new { message = "Choose another approver and a valid delegation period." });
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid(); var item = new ApprovalDelegation { FromUserId = request.FromUserId, ToUserId = request.ToUserId, DivisionId = request.DivisionId, StartsAt = request.StartsAt, EndsAt = request.EndsAt };
        db.ApprovalDelegations.Add(item); AuditWriter.Record(db, scope, "Approval delegated", nameof(ApprovalDelegation), item.Id, $"Approval authority delegated until {item.EndsAt:d}.", null); await db.SaveChangesAsync(token); return Ok(item);
    }

    private static ApprovalWorkflowStage MapStage(ApprovalWorkflowStageRequest x) => new() { Sequence = x.Sequence, Name = x.Name.Trim(), AssignedRole = Clean(x.AssignedRole), AssignedUserId = x.AssignedUserId, EscalateAfterHours = x.EscalateAfterHours, EscalationRole = Clean(x.EscalationRole) };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ApprovalWorkflowStageRequest([Range(1, 5)] int Sequence, [Required] string Name, string? AssignedRole, Guid? AssignedUserId, [Range(1, 720)] int EscalateAfterHours = 24, string? EscalationRole = null);
public sealed record SaveApprovalWorkflowRequest([Required] string Name, ApprovalType Type, string? EntityType, Guid? BranchId, Guid? DivisionId, bool IsActive, IReadOnlyList<ApprovalWorkflowStageRequest> Stages);
public sealed record ApprovalDelegationRequest(Guid FromUserId, Guid ToUserId, Guid? DivisionId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
