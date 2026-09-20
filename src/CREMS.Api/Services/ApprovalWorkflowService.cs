using CREMS.Api.Data;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class ApprovalWorkflowService
{
    public sealed record BookingApprovalItem(AssetType AssetType, DateTimeOffset StartAt, DateTimeOffset EndAt);
    public sealed record BookingApprovalContext(Guid BranchId, Guid? DivisionId, decimal Amount, bool IsEquipment, bool HasPersonnel, bool HasOvertime, IReadOnlyList<BookingApprovalItem> Items);
    public sealed record BookingApprovalMatch(Guid WorkflowId, string WorkflowName, string Reason, IReadOnlyList<ApprovalWorkflowStage> Stages);

    public static async Task<BookingApprovalMatch?> MatchBookingAsync(ApplicationDbContext db, BookingApprovalContext context, CancellationToken token, bool forQuotation = false)
    {
        var candidates = await db.ApprovalWorkflows.AsNoTracking().Include(x => x.Stages)
            .Where(x => x.IsActive && x.Type == ApprovalType.Booking && (forQuotation ? x.AppliesToQuotation : x.AppliesToBooking) &&
                (x.BranchId == null || x.BranchId == context.BranchId) &&
                (x.DivisionId == null || x.DivisionId == context.DivisionId))
            .ToListAsync(token);

        return SelectBookingMatch(candidates, context);
    }

    public static BookingApprovalMatch? SelectBookingMatch(IEnumerable<ApprovalWorkflow> candidates, BookingApprovalContext context)
    {
        var workflow = candidates.Where(x => MatchesBooking(x, context)).OrderByDescending(x => x.BranchId.HasValue)
            .ThenByDescending(x => x.DivisionId.HasValue).ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.MinimumAmount ?? 0).FirstOrDefault();
        if (workflow is null) return null;

        return new BookingApprovalMatch(workflow.Id, workflow.Name, DescribeConditions(workflow), workflow.Stages.OrderBy(x => x.Sequence).ToList());
    }

    public static bool MatchesBooking(ApprovalWorkflow workflow, BookingApprovalContext context)
    {
        var matches = new List<bool>();
        if (workflow.IsDefaultForBookings) matches.Add(true);
        if (workflow.MinimumAmount.HasValue) matches.Add(context.Amount >= workflow.MinimumAmount.Value);
        if (workflow.TriggerForEquipment) matches.Add(context.IsEquipment);
        if (workflow.TriggerForPersonnel) matches.Add(context.HasPersonnel);
        if (workflow.TriggerForOvertime) matches.Add(context.HasOvertime);

        var hasAssetType = workflow.AssetTypeCondition.HasValue;
        var hasDuration = workflow.HireDurationOperator.HasValue && workflow.HireDurationDays.HasValue;
        if (hasAssetType && hasDuration && workflow.ConditionMatchMode == ApprovalConditionMatchMode.All)
            matches.Add(context.Items.Any(item => MatchesAssetType(workflow, item) && MatchesDuration(workflow, item)));
        else
        {
            if (hasAssetType) matches.Add(context.Items.Any(item => MatchesAssetType(workflow, item)));
            if (hasDuration) matches.Add(context.Items.Any(item => MatchesDuration(workflow, item)));
        }

        return matches.Count > 0 && (workflow.ConditionMatchMode == ApprovalConditionMatchMode.All ? matches.All(x => x) : matches.Any(x => x));
    }

    public static string DescribeConditions(ApprovalWorkflow workflow)
    {
        var conditions = new List<string>();
        if (workflow.IsDefaultForBookings) conditions.Add("every booking");
        if (workflow.MinimumAmount.HasValue) conditions.Add($"booking value ≥ FJD {workflow.MinimumAmount.Value:N2}");
        if (workflow.TriggerForEquipment) conditions.Add("equipment hire");
        if (workflow.TriggerForPersonnel) conditions.Add("operator or personnel included");
        if (workflow.TriggerForOvertime) conditions.Add("overtime included");
        if (workflow.AssetTypeCondition.HasValue) conditions.Add($"asset type is {AssetTypeLabel(workflow.AssetTypeCondition.Value)}");
        if (workflow.HireDurationOperator.HasValue && workflow.HireDurationDays.HasValue)
            conditions.Add($"item hire duration {OperatorSymbol(workflow.HireDurationOperator.Value)} {workflow.HireDurationDays.Value:0.##} days");
        return string.Join(workflow.ConditionMatchMode == ApprovalConditionMatchMode.All ? " AND " : " OR ", conditions);
    }

    private static bool MatchesAssetType(ApprovalWorkflow workflow, BookingApprovalItem item) => item.AssetType == workflow.AssetTypeCondition;
    private static bool MatchesDuration(ApprovalWorkflow workflow, BookingApprovalItem item)
    {
        var duration = item.EndAt - item.StartAt;
        var threshold = TimeSpan.FromDays((double)workflow.HireDurationDays!.Value);
        return workflow.HireDurationOperator switch
        {
            CREMS.Api.Domain.Corporate.HireDurationOperator.GreaterThan => duration > threshold,
            CREMS.Api.Domain.Corporate.HireDurationOperator.GreaterThanOrEqual => duration >= threshold,
            CREMS.Api.Domain.Corporate.HireDurationOperator.LessThan => duration < threshold,
            CREMS.Api.Domain.Corporate.HireDurationOperator.LessThanOrEqual => duration <= threshold,
            _ => false,
        };
    }
    private static string OperatorSymbol(HireDurationOperator value) => value switch
    {
        HireDurationOperator.GreaterThan => ">",
        HireDurationOperator.GreaterThanOrEqual => "≥",
        HireDurationOperator.LessThan => "<",
        HireDurationOperator.LessThanOrEqual => "≤",
        _ => string.Empty,
    };
    private static string AssetTypeLabel(AssetType value) => System.Text.RegularExpressions.Regex.Replace(value.ToString(), "([a-z])([A-Z])", "$1 $2");

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
