using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController, Route("api/access-management"), Authorize(Policy = SystemPolicies.ManageUsers)]
public sealed class AccessManagementController(ApplicationDbContext db, UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpGet("catalog")]
    public ActionResult Catalog() => Ok(new { permissions = SystemPermissions.All, scopeTypes = Enum.GetNames<AccessScopeType>(), accountStatuses = Enum.GetNames<AccountLifecycleStatus>() });

    [HttpGet("users/{userId:guid}")]
    public async Task<ActionResult> UserAccess(Guid userId, CancellationToken token)
    {
        var user = await users.FindByIdAsync(userId.ToString()); if (user is null) return NotFound(); if (!await CanManage(user)) return Forbid();
        return Ok(new { user.Id, user.AccountStatus, user.MfaEnabled, user.MfaRequired,
            scopes = await db.UserAccessScopes.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.IsActive).ThenBy(x => x.ExpiresAt).ToListAsync(token),
            permissions = await db.UserPermissionOverrides.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(token),
            sessions = await db.UserSessions.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync(token),
            events = await db.SecurityEvents.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.OccurredAt).Take(100).ToListAsync(token) });
    }

    [HttpPut("users/{userId:guid}/scopes"), Authorize(Policy = SystemPermissions.UsersManageAccess)]
    public async Task<ActionResult> SaveScopes(Guid userId, ScopeSetRequest request, CancellationToken token)
    {
        if (!await HasRecentMfa(token)) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Recent MFA verification is required." });
        var target = await users.FindByIdAsync(userId.ToString()); if (target is null) return NotFound(); if (!await CanManage(target) || target.Id.ToString() == users.GetUserId(User)) return BadRequest(new { message = "Users cannot change their own access scope." });
        if (request.Scopes.Any(x => x.Type == AccessScopeType.GroupWide) && !User.IsInRole(SystemRoles.SuperAdministrator)) return Forbid();
        var current = await db.UserAccessScopes.Where(x => x.UserId == userId && x.IsActive).ToListAsync(token); foreach (var item in current) item.IsActive = false;
        foreach (var scope in request.Scopes) db.UserAccessScopes.Add(new UserAccessScope { UserId = userId, Type = scope.Type, DivisionId = scope.DivisionId, BranchId = scope.BranchId, EffectiveFrom = scope.EffectiveFrom ?? DateTimeOffset.UtcNow, ExpiresAt = scope.ExpiresAt, GrantedByUserId = CurrentId(), Reason = request.Reason.Trim() });
        db.SecurityEvents.Add(Event(userId, SecurityEventType.AccessChanged, "Access scopes replaced")); await db.SaveChangesAsync(token); return NoContent();
    }

    [HttpPut("users/{userId:guid}/permissions"), Authorize(Policy = SystemPermissions.UsersManageAccess)]
    public async Task<ActionResult> SavePermissions(Guid userId, PermissionSetRequest request, CancellationToken token)
    {
        if (!await HasRecentMfa(token)) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Recent MFA verification is required." });
        var target = await users.FindByIdAsync(userId.ToString()); if (target is null) return NotFound(); if (!await CanManage(target) || target.Id.ToString() == users.GetUserId(User)) return BadRequest(new { message = "Users cannot grant permissions to themselves." });
        if (request.Items.Any(x => x.Permission == SystemPermissions.DivisionsConfigure) && !User.IsInRole(SystemRoles.SuperAdministrator)) return Forbid();
        var current = await db.UserPermissionOverrides.Where(x => x.UserId == userId).ToListAsync(token); db.UserPermissionOverrides.RemoveRange(current);
        foreach (var item in request.Items.Where(x => SystemPermissions.All.Contains(x.Permission))) db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = userId, Permission = item.Permission, IsGranted = item.IsGranted, ExpiresAt = item.ExpiresAt, GrantedByUserId = CurrentId(), Reason = request.Reason.Trim() });
        db.SecurityEvents.Add(Event(userId, SecurityEventType.AccessChanged, "Permission overrides replaced")); await db.SaveChangesAsync(token); return NoContent();
    }

    [HttpPut("users/{userId:guid}/lifecycle")]
    public async Task<ActionResult> Lifecycle(Guid userId, AccountLifecycleRequest request, CancellationToken token)
    {
        var target = await users.FindByIdAsync(userId.ToString()); if (target is null) return NotFound(); if (!await CanManage(target) || target.Id.ToString() == users.GetUserId(User)) return BadRequest(new { message = "Users cannot change their own lifecycle state." });
        target.AccountStatus = request.Status; target.SuspensionReason = request.Reason.Trim(); target.IsActive = request.Status is AccountLifecycleStatus.Active or AccountLifecycleStatus.Invited or AccountLifecycleStatus.ActivationPending;
        if (request.Status == AccountLifecycleStatus.Suspended) target.SuspendedAt = DateTimeOffset.UtcNow; if (request.Status == AccountLifecycleStatus.Deactivated) target.DeactivatedAt = DateTimeOffset.UtcNow;
        target.MfaRequired = request.MfaRequired; await users.UpdateAsync(target); if (!target.IsActive) await users.UpdateSecurityStampAsync(target);
        db.SecurityEvents.Add(Event(userId, request.Status == AccountLifecycleStatus.Suspended ? SecurityEventType.AccountSuspended : request.Status == AccountLifecycleStatus.Deactivated ? SecurityEventType.AccountDeactivated : SecurityEventType.AccessChanged, $"Lifecycle changed to {request.Status}: {request.Reason}")); await db.SaveChangesAsync(token); return NoContent();
    }

    [HttpPost("users/{userId:guid}/departure")]
    public async Task<ActionResult> Departure(Guid userId, DepartureRequest request, CancellationToken token)
    {
        var target = await users.FindByIdAsync(userId.ToString()); if (target is null) return NotFound(); if (!await CanManage(target) || target.Id.ToString() == users.GetUserId(User)) return Forbid();
        target.IsActive = false; target.AccountStatus = AccountLifecycleStatus.Deactivated; target.DeactivatedAt = DateTimeOffset.UtcNow; target.SuspensionReason = request.Reason.Trim(); await users.UpdateAsync(target); await users.UpdateSecurityStampAsync(target);
        foreach (var scope in await db.UserAccessScopes.Where(x => x.UserId == userId && x.IsActive).ToListAsync(token)) scope.IsActive = false;
        foreach (var session in await db.UserSessions.Where(x => x.UserId == userId && x.EndedAt == null).ToListAsync(token)) { session.EndedAt = DateTimeOffset.UtcNow; session.EndReason = "Staff departure"; }
        foreach (var decision in await db.ApprovalStageDecisions.Where(x => x.AssignedUserId == userId && x.Status == Domain.Corporate.ApprovalStatus.Pending).ToListAsync(token)) decision.AssignedUserId = request.ReassignApprovalsToUserId;
        db.SecurityEvents.Add(Event(userId, SecurityEventType.AccountDeactivated, $"Departure processed: {request.Reason}")); await db.SaveChangesAsync(token); return NoContent();
    }

    private Guid CurrentId() => Guid.Parse(users.GetUserId(User)!);
    private async Task<bool> CanManage(ApplicationUser target) => User.IsInRole(SystemRoles.SuperAdministrator) || !await users.IsInRoleAsync(target, SystemRoles.SuperAdministrator);
    private SecurityEvent Event(Guid userId, SecurityEventType type, string detail) => new() { UserId = userId, Type = type, Succeeded = true, Detail = detail, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent = Request.Headers.UserAgent.ToString() };
    private async Task<bool> HasRecentMfa(CancellationToken token) => await db.SecurityEvents.AnyAsync(x => x.UserId == CurrentId() && x.Type == SecurityEventType.MfaSucceeded && x.Succeeded && x.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-15), token);
}

public sealed record ScopeItem(AccessScopeType Type, Guid? DivisionId, Guid? BranchId, DateTimeOffset? EffectiveFrom, DateTimeOffset? ExpiresAt);
public sealed record ScopeSetRequest(IReadOnlyList<ScopeItem> Scopes, string Reason);
public sealed record PermissionItem(string Permission, bool IsGranted, DateTimeOffset? ExpiresAt);
public sealed record PermissionSetRequest(IReadOnlyList<PermissionItem> Items, string Reason);
public sealed record AccountLifecycleRequest(AccountLifecycleStatus Status, bool MfaRequired, string Reason);
public sealed record DepartureRequest(string Reason, Guid? ReassignApprovalsToUserId);
