using CREMS.Api.Data;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class ApprovalWorkflowService
{
    public sealed record BookingApprovalContext(Guid BranchId, Guid? DivisionId, decimal Amount, bool IsEquipment, bool HasPersonnel, bool HasOvertime);
    public sealed record BookingApprovalMatch(Guid WorkflowId, string WorkflowName, string Reason, IReadOnlyList<ApprovalWorkflowStage> Stages);

    public static async Task<BookingApprovalMatch?> MatchBookingAsync(ApplicationDbContext db, BookingApprovalContext context, CancellationToken token)
    {
        var candidates = await db.ApprovalWorkflows.AsNoTracking().Include(x => x.Stages)
            .Where(x => x.IsActive && x.Type == ApprovalType.Booking &&
                (x.BranchId == null || x.BranchId == context.BranchId) &&
                (x.DivisionId == null || x.DivisionId == context.DivisionId))
            .ToListAsync(token);

        var matches = candidates.Where(x =>
            x.IsDefaultForBookings ||
            (x.MinimumAmount.HasValue && context.Amount >= x.MinimumAmount.Value) ||
            (x.TriggerForEquipment && context.IsEquipment) ||
            (x.TriggerForPersonnel && context.HasPersonnel) ||
            (x.TriggerForOvertime && context.HasOvertime));
        var workflow = matches.OrderByDescending(x => x.BranchId.HasValue)
            .ThenByDescending(x => x.DivisionId.HasValue).ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.MinimumAmount ?? 0).FirstOrDefault();
        if (workflow is null) return null;

        var reasons = new List<string>();
        if (workflow.IsDefaultForBookings) reasons.Add("configured for every booking");
        if (workflow.MinimumAmount.HasValue && context.Amount >= workflow.MinimumAmount.Value) reasons.Add($"value is FJD {context.Amount:N2}");
        if (workflow.TriggerForEquipment && context.IsEquipment) reasons.Add("equipment hire");
        if (workflow.TriggerForPersonnel && context.HasPersonnel) reasons.Add("operator or personnel included");
        if (workflow.TriggerForOvertime && context.HasOvertime) reasons.Add("overtime included");
        return new BookingApprovalMatch(workflow.Id, workflow.Name, string.Join(", ", reasons), workflow.Stages.OrderBy(x => x.Sequence).ToList());
    }

    public static void ConfigureFromMatch(ApprovalRequest request, BookingApprovalMatch match)
    {
        request.WorkflowId = match.WorkflowId;
        request.TotalStages = match.Stages.Count;
        foreach (var stage in match.Stages)
            request.StageDecisions.Add(new ApprovalStageDecision { StageNumber = stage.Sequence, StageName = stage.Name, AssignedRole = stage.AssignedRole, AssignedUserId = stage.AssignedUserId });
    }

    public static void RecordRentalOfficerReview(ApprovalRequest request, Guid reviewerUserId)
    {
        var firstStage = request.StageDecisions.OrderBy(x => x.StageNumber).FirstOrDefault();
        if (firstStage?.AssignedRole != SystemRoles.RentalOfficer) return;
        firstStage.Status = ApprovalStatus.Approved;
        firstStage.DecidedByUserId = reviewerUserId;
        firstStage.DecisionNote = "Booking checked and submitted by the rental officer.";
        firstStage.DecidedAt = DateTimeOffset.UtcNow;
        request.CurrentStage = request.TotalStages > 1 ? firstStage.StageNumber + 1 : firstStage.StageNumber;
        if (request.TotalStages == 1)
        {
            request.Status = ApprovalStatus.Approved;
            request.DecidedByUserId = reviewerUserId;
            request.DecidedAt = firstStage.DecidedAt;
            request.DecisionNote = firstStage.DecisionNote;
        }
    }

    public static async Task ConfigureAsync(ApplicationDbContext db, ApprovalRequest request, Guid? divisionId, CancellationToken token)
    {
        var candidates = await db.ApprovalWorkflows.AsNoTracking().Include(x => x.Stages)
            .Where(x => x.IsActive && x.Type == request.Type && (x.EntityType == null || x.EntityType == request.EntityType) &&
                (x.BranchId == null || x.BranchId == request.BranchId) && (x.DivisionId == null || x.DivisionId == divisionId))
            .ToListAsync(token);
        var workflow = candidates.OrderByDescending(x => x.BranchId.HasValue).ThenByDescending(x => x.DivisionId.HasValue).ThenByDescending(x => x.EntityType != null).FirstOrDefault();
        var stages = workflow?.Stages.OrderBy(x => x.Sequence).ToList() ?? [];
        if (stages.Count == 0)
        {
            request.TotalStages = 1;
            request.StageDecisions.Add(new ApprovalStageDecision { StageNumber = 1, StageName = "Manager approval", AssignedRole = "BranchManager" });
            return;
        }
        request.WorkflowId = workflow!.Id; request.TotalStages = stages.Count;
        foreach (var stage in stages) request.StageDecisions.Add(new ApprovalStageDecision { StageNumber = stage.Sequence, StageName = stage.Name, AssignedRole = stage.AssignedRole, AssignedUserId = stage.AssignedUserId });
    }
}
