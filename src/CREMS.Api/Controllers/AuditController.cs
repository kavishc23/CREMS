using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = SystemPolicies.ViewReports)]
public sealed class AuditController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> GetAll([FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var query = db.AuditEvents.AsNoTracking();
        if (!scope.IsAdministrator) query = query.Where(item => item.BranchId == scope.BranchId);
        var events = await query.OrderByDescending(item => item.OccurredAt).Take(Math.Clamp(limit, 1, 500))
            .Select(item => new { item.Id, item.OccurredAt, item.UserName, item.Action, item.EntityType,
                item.EntityId, item.Summary, item.BranchId, BranchName = item.Branch != null ? item.Branch.Name : null,
                item.PreviousValues, item.NewValues })
            .ToListAsync(cancellationToken);
        return Ok(events);
    }
}
