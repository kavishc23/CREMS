using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;

namespace CREMS.Api.Domain.Common;

public static class AuditWriter
{
    public static void Record(ApplicationDbContext db, StaffDataScope scope, string action,
        string entityType, Guid entityId, string summary, Guid? branchId,
        string? previousValues = null, string? newValues = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            BranchId = branchId,
            UserId = scope.UserId,
            UserName = scope.UserName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            PreviousValues = previousValues,
            NewValues = newValues,
        });
    }
}
