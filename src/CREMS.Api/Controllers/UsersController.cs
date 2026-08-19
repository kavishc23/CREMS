using System.Security.Cryptography;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = SystemPolicies.ManageUsers)]
public sealed class UsersController(UserManager<ApplicationUser> userManager, ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll(CancellationToken token)
    {
        var users = await userManager.Users.AsNoTracking().OrderBy(x => x.FullName).ThenBy(x => x.Email).ToListAsync(token);
        var response = new List<UserResponse>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            if (!roles.Contains(SystemRoles.Customer)) response.Add(ToResponse(user, roles));
        }
        return Ok(response);
    }

    [HttpGet("summary")]
    public async Task<ActionResult> Summary(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var customerRoleId = await db.Roles.Where(x => x.Name == SystemRoles.Customer).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(token);
        var staff = db.Users.Where(x => !customerRoleId.HasValue || !db.UserRoles.Any(link => link.UserId == x.Id && link.RoleId == customerRoleId.Value));
        return Ok(new { totalUsers = await staff.CountAsync(token), activeUsers = await staff.CountAsync(x => x.IsActive, token), disabledUsers = await staff.CountAsync(x => !x.IsActive, token), lockedUsers = await staff.CountAsync(x => x.LockoutEnd != null && x.LockoutEnd > now, token), passwordChangeRequired = await staff.CountAsync(x => x.MustChangePassword, token), administrators = await ActiveAdministratorCount(token) });
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request)
    {
        var role = ResolveRole(request.Role); if (role is null) return Invalid(nameof(request.Role), "Select a valid CREMS role.");
        if (role == SystemRoles.SuperAdministrator && !User.IsInRole(SystemRoles.SuperAdministrator)) return Forbid();
        var requiresBranch = RequiresBranch(role);
        if (requiresBranch && !await IsValidDivision(request.DivisionId)) return Invalid(nameof(request.DivisionId), "Select an active division for this staff account.");
        if (requiresBranch && !await IsValidBranch(request.BranchId)) return Invalid(nameof(request.BranchId), "Select an active branch for this staff account.");
        if (requiresBranch && !await IsDivisionAtBranch(request.DivisionId, request.BranchId)) return Invalid(nameof(request.BranchId), "The selected branch is not enabled for this division.");
        var email = request.Email.Trim(); if (await userManager.FindByEmailAsync(email) is not null) return Invalid(nameof(request.Email), "An account with this email already exists.");
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = request.FullName.Trim(), DivisionId = requiresBranch ? request.DivisionId : null, BranchId = requiresBranch ? request.BranchId : null, IsActive = true, MustChangePassword = true, AdminNote = Clean(request.AdminNote) };
        var created = await userManager.CreateAsync(user, request.Password); if (!created.Succeeded) return IdentityErrors(created);
        var assigned = await userManager.AddToRoleAsync(user, role); if (!assigned.Succeeded) { await userManager.DeleteAsync(user); return IdentityErrors(assigned); }
        await Audit(user, "Staff account created", $"{user.Email} was created as {role}.");
        return CreatedAtAction(nameof(GetAll), ToResponse(user, [role]));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, UpdateUserRequest request, CancellationToken token)
    {
        if (!await HasRecentMfa(token)) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Re-authenticate with MFA before changing roles or access." });
        var user = await userManager.FindByIdAsync(id.ToString()); if (user is null) return NotFound();
        if (!await CanManage(user)) return Forbid();
        var role = ResolveRole(request.Role); if (role is null) return Invalid(nameof(request.Role), "Select a valid CREMS role.");
        if (role == SystemRoles.SuperAdministrator && !User.IsInRole(SystemRoles.SuperAdministrator)) return Forbid();
        var requiresBranch = RequiresBranch(role); if (requiresBranch && !await IsValidBranch(request.BranchId)) return Invalid(nameof(request.BranchId), "Select an active branch for this staff account.");
        if (requiresBranch && !await IsValidDivision(request.DivisionId)) return Invalid(nameof(request.DivisionId), "Select an active division for this staff account.");
        if (requiresBranch && !await IsDivisionAtBranch(request.DivisionId, request.BranchId)) return Invalid(nameof(request.BranchId), "The selected branch is not enabled for this division.");
        var currentRoles = await userManager.GetRolesAsync(user);
        if (user.Id.ToString() == userManager.GetUserId(User) && (!request.IsActive || !SystemRoles.GroupWide.Contains(role))) return Invalid(nameof(request.IsActive), "You cannot disable or demote your own administrator account.");
        if (currentRoles.Contains(SystemRoles.SuperAdministrator) && (!request.IsActive || role != SystemRoles.SuperAdministrator) && await ActiveSuperAdministratorCount(token) <= 1) return Invalid(nameof(request.Role), "The final active Super Administrator cannot be disabled or demoted.");
        var email = request.Email.Trim(); var duplicate = await userManager.FindByEmailAsync(email); if (duplicate is not null && duplicate.Id != id) return Invalid(nameof(request.Email), "An account with this email already exists.");
        var previous = $"Email={user.Email}; Role={string.Join(',', currentRoles)}; Branch={user.BranchId}; Active={user.IsActive}";
        user.FullName = request.FullName.Trim(); user.Email = email; user.UserName = email; user.NormalizedEmail = userManager.NormalizeEmail(email); user.NormalizedUserName = userManager.NormalizeName(email); user.EmailConfirmed = true; user.DivisionId = requiresBranch ? request.DivisionId : null; user.BranchId = requiresBranch ? request.BranchId : null; user.IsActive = request.IsActive; user.AdminNote = Clean(request.AdminNote); user.SuspensionReason = request.IsActive ? null : Clean(request.SuspensionReason);
        var updated = await userManager.UpdateAsync(user); if (!updated.Succeeded) return IdentityErrors(updated);
        var removed = await userManager.RemoveFromRolesAsync(user, currentRoles.Where(x => x != role)); if (!removed.Succeeded) return IdentityErrors(removed);
        if (!await userManager.IsInRoleAsync(user, role)) { var added = await userManager.AddToRoleAsync(user, role); if (!added.Succeeded) return IdentityErrors(added); }
        if (!request.IsActive) await userManager.UpdateSecurityStampAsync(user);
        await Audit(user, "Staff account updated", $"{user.Email} role={role}, active={user.IsActive}.", previous, $"Email={user.Email}; Role={role}; Branch={user.BranchId}; Active={user.IsActive}");
        return Ok(ToResponse(user, [role]));
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<ActionResult> ResetPassword(Guid id, AdminReasonRequest request)
    {
        if (!await HasRecentMfa(default)) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Re-authenticate with MFA before resetting a password." });
        var user = await userManager.FindByIdAsync(id.ToString()); if (user is null) return NotFound();
        if (!await CanManage(user)) return Forbid();
        var temporaryPassword = GenerateTemporaryPassword(); var token = await userManager.GeneratePasswordResetTokenAsync(user); var result = await userManager.ResetPasswordAsync(user, token, temporaryPassword); if (!result.Succeeded) return IdentityErrors(result);
        user.MustChangePassword = true; user.PasswordChangedAt = DateTimeOffset.UtcNow; await userManager.UpdateAsync(user); await userManager.UpdateSecurityStampAsync(user);
        await Audit(user, "Staff password reset", $"A temporary password was generated for {user.Email}. Reason: {Clean(request.Reason) ?? "Not supplied"}. Sessions were revoked.");
        return Ok(new { temporaryPassword, mustChangePassword = true });
    }

    [HttpPost("{id:guid}/unlock")]
    public async Task<ActionResult> Unlock(Guid id, AdminReasonRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString()); if (user is null) return NotFound(); if (!await CanManage(user)) return Forbid(); await userManager.ResetAccessFailedCountAsync(user); await userManager.SetLockoutEndDateAsync(user, null); await Audit(user, "Staff account unlocked", Reason(request)); return NoContent();
    }

    [HttpPost("{id:guid}/revoke-sessions")]
    public async Task<ActionResult> RevokeSessions(Guid id, AdminReasonRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString()); if (user is null) return NotFound(); if (!await CanManage(user)) return Forbid(); await userManager.UpdateSecurityStampAsync(user); await Audit(user, "Staff sessions revoked", Reason(request)); return NoContent();
    }

    private async Task<int> ActiveAdministratorCount(CancellationToken token)
    {
        var role = await db.Roles.FirstAsync(x => x.Name == SystemRoles.Administrator, token);
        return await (from user in db.Users join link in db.UserRoles on user.Id equals link.UserId where link.RoleId == role.Id && user.IsActive select user).CountAsync(token);
    }
    private async Task<int> ActiveSuperAdministratorCount(CancellationToken token)
    {
        var role = await db.Roles.FirstAsync(x => x.Name == SystemRoles.SuperAdministrator, token);
        return await (from user in db.Users join link in db.UserRoles on user.Id equals link.UserId where link.RoleId == role.Id && user.IsActive select user).CountAsync(token);
    }
    private async Task<bool> CanManage(ApplicationUser target) =>
        User.IsInRole(SystemRoles.SuperAdministrator) || !await userManager.IsInRoleAsync(target, SystemRoles.SuperAdministrator);
    private async Task<bool> HasRecentMfa(CancellationToken token) { var id = userManager.GetUserId(User); return Guid.TryParse(id, out var userId) && await db.SecurityEvents.AnyAsync(x => x.UserId == userId && x.Type == SecurityEventType.MfaSucceeded && x.Succeeded && x.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-15), token); }
    private async Task<bool> IsValidBranch(Guid? id) => id.HasValue && await db.Branches.AnyAsync(x => x.Id == id && x.IsActive);
    private async Task<bool> IsValidDivision(Guid? id) => id.HasValue && await db.Divisions.AnyAsync(x => x.Id == id && x.IsActive);
    private async Task<bool> IsDivisionAtBranch(Guid? divisionId, Guid? branchId) => divisionId.HasValue && branchId.HasValue && await db.BranchDivisions.AnyAsync(x => x.DivisionId == divisionId && x.BranchId == branchId && x.IsActive);
    private async Task Audit(ApplicationUser user, string action, string summary, string? previous = null, string? next = null) { var scope = await staffScope.GetAsync(User); if (scope is null) return; AuditWriter.Record(db, scope, action, "ApplicationUser", user.Id, summary, user.BranchId, previous, next); await db.SaveChangesAsync(); }
    private ActionResult Invalid(string key, string message) { ModelState.AddModelError(key, message); return ValidationProblem(ModelState); }
    private ActionResult IdentityErrors(IdentityResult result) { foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description); return ValidationProblem(ModelState); }
    private static string? ResolveRole(string role) => SystemRoles.Staff.FirstOrDefault(x => string.Equals(x, role, StringComparison.OrdinalIgnoreCase));
    private static bool RequiresBranch(string role) => SystemRoles.BranchScoped.Contains(role);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Reason(AdminReasonRequest request) => $"Reason: {Clean(request.Reason) ?? "Not supplied"}.";
    private static UserResponse ToResponse(ApplicationUser user, IEnumerable<string> roles) => new(user.Id, user.Email ?? "", user.FullName, user.DivisionId, user.BranchId, user.IsActive, roles.ToArray(), user.MustChangePassword, user.LastLoginAt, user.LastLoginIp, user.LastActivityAt, user.AccessFailedCount, user.LockoutEnd, user.AdminNote, user.SuspensionReason);
    private static string GenerateTemporaryPassword() { const string lower = "abcdefghijkmnopqrstuvwxyz", upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", digits = "23456789", symbols = "!@$%*?", all = lower + upper + digits + symbols; var chars = new[] { lower[RandomNumberGenerator.GetInt32(lower.Length)], upper[RandomNumberGenerator.GetInt32(upper.Length)], digits[RandomNumberGenerator.GetInt32(digits.Length)], symbols[RandomNumberGenerator.GetInt32(symbols.Length)] }.Concat(Enumerable.Range(0, 12).Select(_ => all[RandomNumberGenerator.GetInt32(all.Length)])).OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray(); return new string(chars); }
}

public sealed record CreateUserRequest(string FullName, string Email, string Password, string Role, Guid? DivisionId, Guid? BranchId, string? AdminNote);
public sealed record UpdateUserRequest(string FullName, string Email, string Role, Guid? DivisionId, Guid? BranchId, bool IsActive, string? AdminNote, string? SuspensionReason);
public sealed record AdminReasonRequest(string? Reason);
public sealed record UserResponse(Guid Id, string Email, string FullName, Guid? DivisionId, Guid? BranchId, bool IsActive, IReadOnlyCollection<string> Roles, bool MustChangePassword, DateTimeOffset? LastLoginAt, string? LastLoginIp, DateTimeOffset? LastActivityAt, int AccessFailedCount, DateTimeOffset? LockoutEnd, string? AdminNote, string? SuspensionReason);
