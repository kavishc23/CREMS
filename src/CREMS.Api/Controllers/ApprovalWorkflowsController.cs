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
[Route("api/approval-workflows")]
[Authorize(Policy = SystemPolicies.AdministerSystem)]
public sealed class ApprovalWorkflowsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(CancellationToken token)
    {
        var workflows = await db.ApprovalWorkflows.AsNoTracking().Include(x => x.Stages).OrderBy(x => x.Type).ThenBy(x => x.Name).ToListAsync(token);
        return Ok(workflows.Select(ToResponse));
    }

    [HttpGet("booking-condition-options")]
    public ActionResult BookingConditionOptions() => Ok(new {
        assetTypes = Enum.GetValues<AssetType>().Select(x => new { value = x, label = SplitWords(x.ToString()) }),
        durationOperators = Enum.GetValues<HireDurationOperator>().Select(x => new { value = x, label = x switch { HireDurationOperator.GreaterThan => "Greater than", HireDurationOperator.GreaterThanOrEqual => "Greater than or equal to", HireDurationOperator.LessThan => "Less than", _ => "Less than or equal to" } }),
        matchModes = Enum.GetValues<ApprovalConditionMatchMode>().Select(x => new { value = x, label = x == ApprovalConditionMatchMode.All ? "Match all conditions (AND)" : "Match any condition (OR)" }),
    });

    [HttpPost]
    public async Task<ActionResult> Create(SaveApprovalWorkflowRequest request, CancellationToken token)
    {
        var conditionErrors = ValidateConditions(request); if (conditionErrors.Count > 0) return BadRequest(new ValidationProblemDetails(conditionErrors));
        if (request.Stages.Count is < 1 or > 5 || request.Stages.Select(x => x.Sequence).Distinct().Count() != request.Stages.Count)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["stages"] = ["Configure between one and five uniquely ordered approval phases."] }));
        if (request.Type == ApprovalType.Booking && request.Stages.Any(x => x.AssignedRole is not (SystemRoles.RentalOfficer or SystemRoles.BranchManager)))
            return BadRequest(new { message = "Booking approval stages can only be assigned to rental officers and branch managers." });
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        var workflow = new ApprovalWorkflow { Name = request.Name.Trim(), Type = request.Type, EntityType = Clean(request.EntityType), BranchId = request.BranchId, DivisionId = request.DivisionId, MinimumAmount = request.MinimumAmount, TriggerForEquipment = request.TriggerForEquipment, AssetTypeCondition = request.AssetTypeCondition, HireDurationOperator = request.HireDurationOperator, HireDurationDays = request.HireDurationDays, ConditionMatchMode = request.ConditionMatchMode, TriggerForPersonnel = request.TriggerForPersonnel, TriggerForOvertime = request.TriggerForOvertime, IsDefaultForBookings = request.IsDefaultForBookings, AppliesToBooking = request.AppliesToBooking, AppliesToQuotation = request.AppliesToQuotation, Priority = request.Priority, IsActive = request.IsActive,
            Stages = request.Stages.OrderBy(x => x.Sequence).Select(MapStage).ToList() };
        db.ApprovalWorkflows.Add(workflow); AuditWriter.Record(db, scope, "Approval workflow created", nameof(ApprovalWorkflow), workflow.Id, $"{workflow.Name} created with {workflow.Stages.Count} phases.", request.BranchId);
        await db.SaveChangesAsync(token); return Ok(ToResponse(workflow));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, SaveApprovalWorkflowRequest request, CancellationToken token)
    {
        var workflow = await db.ApprovalWorkflows.Include(x => x.Stages).FirstOrDefaultAsync(x => x.Id == id, token); if (workflow is null) return NotFound();
        var conditionErrors = ValidateConditions(request); if (conditionErrors.Count > 0) return BadRequest(new ValidationProblemDetails(conditionErrors));
        if (request.Stages.Count is < 1 or > 5 || request.Stages.Select(x => x.Sequence).Distinct().Count() != request.Stages.Count) return BadRequest();
        if (request.Type == ApprovalType.Booking && request.Stages.Any(x => x.AssignedRole is not (SystemRoles.RentalOfficer or SystemRoles.BranchManager)))
            return BadRequest(new { message = "Booking approval stages can only be assigned to rental officers and branch managers." });
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        workflow.Name = request.Name.Trim(); workflow.Type = request.Type; workflow.EntityType = Clean(request.EntityType); workflow.BranchId = request.BranchId; workflow.DivisionId = request.DivisionId; workflow.MinimumAmount = request.MinimumAmount; workflow.TriggerForEquipment = request.TriggerForEquipment; workflow.AssetTypeCondition = request.AssetTypeCondition; workflow.HireDurationOperator = request.HireDurationOperator; workflow.HireDurationDays = request.HireDurationDays; workflow.ConditionMatchMode = request.ConditionMatchMode; workflow.TriggerForPersonnel = request.TriggerForPersonnel; workflow.TriggerForOvertime = request.TriggerForOvertime; workflow.IsDefaultForBookings = request.IsDefaultForBookings; workflow.AppliesToBooking = request.AppliesToBooking; workflow.AppliesToQuotation = request.AppliesToQuotation; workflow.Priority = request.Priority; workflow.IsActive = request.IsActive;
        SynchronizeStages(workflow, request.Stages); workflow.UpdatedAt = DateTimeOffset.UtcNow;
        AuditWriter.Record(db, scope, "Approval workflow updated", nameof(ApprovalWorkflow), workflow.Id, $"{workflow.Name} now has {workflow.Stages.Count} phases.", request.BranchId); await db.SaveChangesAsync(token); return Ok(ToResponse(workflow));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken token)
    {
        var workflow = await db.ApprovalWorkflows.Include(x => x.Stages).FirstOrDefaultAsync(x => x.Id == id, token);
        if (workflow is null) return NotFound();
        if (await db.ApprovalRequests.AnyAsync(x => x.WorkflowId == id, token))
            return Conflict(new { message = "This rule has approval history and cannot be deleted. Deactivate it instead to retain the audit trail." });
        var scope = await staffScope.GetAsync(User); if (scope is null) return Forbid();
        db.ApprovalWorkflows.Remove(workflow);
        AuditWriter.Record(db, scope, "Approval workflow deleted", nameof(ApprovalWorkflow), workflow.Id, $"{workflow.Name} deleted.", workflow.BranchId);
        await db.SaveChangesAsync(token);
        return NoContent();
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
    private void SynchronizeStages(ApprovalWorkflow workflow, IReadOnlyList<ApprovalWorkflowStageRequest> requestedStages)
    {
        var requestedBySequence = requestedStages.ToDictionary(x => x.Sequence);
        foreach (var existing in workflow.Stages.ToList())
        {
            if (!requestedBySequence.TryGetValue(existing.Sequence, out var requested))
            {
                db.ApprovalWorkflowStages.Remove(existing);
                continue;
            }

            existing.Name = requested.Name.Trim();
            existing.AssignedRole = Clean(requested.AssignedRole);
            existing.AssignedUserId = requested.AssignedUserId;
            existing.EscalateAfterHours = requested.EscalateAfterHours;
            existing.EscalationRole = Clean(requested.EscalationRole);
            requestedBySequence.Remove(existing.Sequence);
        }

        foreach (var requested in requestedBySequence.Values)
            workflow.Stages.Add(MapStage(requested));
    }
    public static Dictionary<string, string[]> ValidateConditions(SaveApprovalWorkflowRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.HireDurationOperator.HasValue != request.HireDurationDays.HasValue || request.HireDurationDays is <= 0)
            errors["hireDuration"] = ["Choose a duration operator and enter a positive number of days together."];
        if (request.AssetTypeCondition.HasValue && !Enum.IsDefined(request.AssetTypeCondition.Value))
            errors["assetTypeCondition"] = ["Choose a supported asset type."];
        if (!request.AppliesToBooking && !request.AppliesToQuotation) errors["requestTypes"] = ["Select rental booking, quotation request, or both."];
        if (!Enum.IsDefined(request.ConditionMatchMode)) errors["conditionMatchMode"] = ["Choose whether all or any conditions must match."];
        if (request.Type == ApprovalType.Booking && !request.IsDefaultForBookings && request.MinimumAmount is null && !request.TriggerForEquipment && !request.TriggerForPersonnel && !request.TriggerForOvertime && request.AssetTypeCondition is null && request.HireDurationOperator is null)
            errors["conditions"] = ["Configure at least one booking condition or apply the rule to every booking."];
        return errors;
    }
    private static object ToResponse(ApprovalWorkflow x) => new { x.Id, x.Name, x.Type, x.EntityType, x.BranchId, x.DivisionId, x.MinimumAmount, x.TriggerForEquipment, x.AssetTypeCondition, x.HireDurationOperator, x.HireDurationDays, x.ConditionMatchMode, x.TriggerForPersonnel, x.TriggerForOvertime, x.IsDefaultForBookings, x.AppliesToBooking, x.AppliesToQuotation, x.Priority, x.IsActive, x.CreatedAt, x.UpdatedAt, conditionSummary = ApprovalWorkflowService.DescribeConditions(x), stages = x.Stages.OrderBy(s => s.Sequence).Select(s => new { s.Id, s.Sequence, s.Name, s.AssignedRole, s.AssignedUserId, s.EscalateAfterHours, s.EscalationRole }) };
    private static string SplitWords(string value) => System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ApprovalWorkflowStageRequest([Range(1, 5)] int Sequence, [Required] string Name, string? AssignedRole, Guid? AssignedUserId, [Range(1, 720)] int EscalateAfterHours = 24, string? EscalationRole = null);
public sealed record SaveApprovalWorkflowRequest([Required] string Name, ApprovalType Type, string? EntityType, Guid? BranchId, Guid? DivisionId, bool IsActive, IReadOnlyList<ApprovalWorkflowStageRequest> Stages, decimal? MinimumAmount = null, bool TriggerForEquipment = false, bool TriggerForPersonnel = false, bool TriggerForOvertime = false, bool IsDefaultForBookings = false, [Range(0, 1000)] int Priority = 0, AssetType? AssetTypeCondition = null, HireDurationOperator? HireDurationOperator = null, [Range(typeof(decimal), "0.01", "3650")] decimal? HireDurationDays = null, ApprovalConditionMatchMode ConditionMatchMode = ApprovalConditionMatchMode.Any, bool AppliesToBooking = true, bool AppliesToQuotation = false);
public sealed record ApprovalDelegationRequest(Guid FromUserId, Guid ToUserId, Guid? DivisionId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
