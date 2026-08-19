using CREMS.Api.Data;
using CREMS.Api.Domain.Corporate;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class ApprovalWorkflowService
{
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
