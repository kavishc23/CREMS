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
[Authorize(Policy = SystemPolicies.AdministerSystem)]
public sealed class UsersController(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var users = await userManager.Users
            .AsNoTracking()
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .ToListAsync(cancellationToken);

        var response = new List<UserResponse>(users.Count);
        foreach (var user in users)
        {
            response.Add(new UserResponse(
                user.Id,
                user.Email ?? string.Empty,
                user.FullName,
                user.BranchId,
                user.IsActive,
                (await userManager.GetRolesAsync(user)).ToArray()));
        }

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request)
    {
        var role = SystemRoles.All.FirstOrDefault(
            candidate => string.Equals(candidate, request.Role, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            ModelState.AddModelError(nameof(request.Role), "Select a valid CREMS role.");
            return ValidationProblem(ModelState);
        }

        var requiresBranch = role is SystemRoles.BranchManager or SystemRoles.RentalOfficer;
        if (requiresBranch && !request.BranchId.HasValue)
        {
            ModelState.AddModelError(nameof(request.BranchId), "Branch managers and rental officers must be assigned to a branch.");
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(request.Email), "An account with this email already exists.");
            return ValidationProblem(ModelState);
        }

        if (request.BranchId.HasValue &&
            !await db.Branches.AnyAsync(branch => branch.Id == request.BranchId.Value && branch.IsActive))
        {
            ModelState.AddModelError(nameof(request.BranchId), "The selected branch does not exist.");
            return ValidationProblem(ModelState);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            BranchId = requiresBranch ? request.BranchId : null,
            IsActive = true,
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return ValidationProblem(ModelState);
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            foreach (var error in roleResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return ValidationProblem(ModelState);
        }

        var scope = await staffScope.GetAsync(User);
        if (scope is not null)
        {
            AuditWriter.Record(db, scope, "Staff account created", "ApplicationUser", user.Id,
                $"{user.Email} was created as {role}.", user.BranchId);
            await db.SaveChangesAsync();
        }
        return CreatedAtAction(nameof(GetAll), new UserResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.BranchId,
            user.IsActive,
            [role]));
    }

    [HttpPatch("{id:guid}/branch")]
    public async Task<ActionResult> AssignBranch(
        Guid id,
        AssignUserBranchRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains(SystemRoles.Administrator))
        {
            user.BranchId = null;
        }
        else
        {
            if (!request.BranchId.HasValue || !await db.Branches.AnyAsync(branch =>
                branch.Id == request.BranchId.Value && branch.IsActive, cancellationToken))
            {
                ModelState.AddModelError(nameof(request.BranchId), "Select an active branch for this staff account.");
                return ValidationProblem(ModelState);
            }
            user.BranchId = request.BranchId;
        }

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return ValidationProblem(ModelState);
        }
        var scope = await staffScope.GetAsync(User);
        if (scope is not null)
        {
            AuditWriter.Record(db, scope, "Staff branch assigned", "ApplicationUser", user.Id,
                $"{user.Email} was assigned to branch {user.BranchId}.", user.BranchId);
            await db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var role = SystemRoles.All.FirstOrDefault(candidate =>
            string.Equals(candidate, request.Role, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            ModelState.AddModelError(nameof(request.Role), "Select a valid CREMS role.");
            return ValidationProblem(ModelState);
        }

        var requiresBranch = role is SystemRoles.BranchManager or SystemRoles.RentalOfficer;
        if (requiresBranch && (!request.BranchId.HasValue || !await db.Branches.AnyAsync(branch =>
            branch.Id == request.BranchId.Value && branch.IsActive, cancellationToken)))
        {
            ModelState.AddModelError(nameof(request.BranchId), "Select an active branch for this staff account.");
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null && existing.Id != id)
        {
            ModelState.AddModelError(nameof(request.Email), "An account with this email already exists.");
            return ValidationProblem(ModelState);
        }
        if (user.Id.ToString() == userManager.GetUserId(User) && !request.IsActive)
        {
            ModelState.AddModelError(nameof(request.IsActive), "You cannot disable your own account.");
            return ValidationProblem(ModelState);
        }

        user.FullName = request.FullName.Trim();
        user.Email = email;
        user.UserName = email;
        user.NormalizedEmail = userManager.NormalizeEmail(email);
        user.NormalizedUserName = userManager.NormalizeName(email);
        user.EmailConfirmed = true;
        user.BranchId = requiresBranch ? request.BranchId : null;
        user.IsActive = request.IsActive;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return ValidationProblem(ModelState);
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles.Where(current => current != role));
        if (!removeResult.Succeeded)
        {
            foreach (var error in removeResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return ValidationProblem(ModelState);
        }
        if (!await userManager.IsInRoleAsync(user, role))
        {
            var addResult = await userManager.AddToRoleAsync(user, role);
            if (!addResult.Succeeded)
            {
                foreach (var error in addResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                return ValidationProblem(ModelState);
            }
        }

        AuditWriter.Record(db, scope: (await staffScope.GetAsync(User))!, "Staff account updated", "ApplicationUser", user.Id,
            $"{user.Email} role={role}, active={user.IsActive}.", user.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new UserResponse(user.Id, user.Email ?? string.Empty, user.FullName,
            user.BranchId, user.IsActive, [role]));
    }

    [HttpPatch("{id:guid}/password")]
    public async Task<ActionResult> ResetPassword(Guid id, ResetUserPasswordRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return ValidationProblem(ModelState);
        }
        var scope = await staffScope.GetAsync(User);
        if (scope is not null)
        {
            AuditWriter.Record(db, scope, "Staff password reset", "ApplicationUser", user.Id,
                $"A temporary password was set for {user.Email}.", user.BranchId);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }
}

public sealed record CreateUserRequest(
    string FullName,
    string Email,
    string Password,
    string Role,
    Guid? BranchId);

public sealed record AssignUserBranchRequest(Guid? BranchId);
public sealed record UpdateUserRequest(
    string FullName,
    string Email,
    string Role,
    Guid? BranchId,
    bool IsActive);
public sealed record ResetUserPasswordRequest(string Password);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string FullName,
    Guid? BranchId,
    bool IsActive,
    IReadOnlyCollection<string> Roles);
