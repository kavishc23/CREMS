using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class SessionController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    CREMS.Api.Data.ApplicationDbContext db) : ControllerBase
{
    [HttpGet("session")]
    [Authorize(Policy = SystemPolicies.StaffPortal)]
    public async Task<ActionResult<SessionResponse>> GetSession()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        var branchName = user.BranchId.HasValue ? await db.Branches.Where(x => x.Id == user.BranchId).Select(x => x.Name).FirstOrDefaultAsync() : null;
        var divisionName = user.DivisionId.HasValue ? await db.Divisions.Where(x => x.Id == user.DivisionId).Select(x => x.Name).FirstOrDefaultAsync() : null;
        var now = DateTimeOffset.UtcNow;
        if (!user.LastActivityAt.HasValue || now - user.LastActivityAt.Value > TimeSpan.FromMinutes(5))
        {
            if (!user.LastLoginAt.HasValue) user.LastLoginAt = now;
            user.LastActivityAt = now;
            user.LastLoginIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            await userManager.UpdateAsync(user);
        }
        return Ok(new SessionResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            user.BranchId,
            user.DivisionId,
            branchName,
            divisionName,
            roles.ToArray(),
            user.MustChangePassword));
    }

    [HttpPost("change-password")]
    [Authorize(Policy = SystemPolicies.StaffPortal)]
    public async Task<ActionResult> ChangePassword(ChangeOwnPasswordRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive) return Unauthorized();
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return ValidationProblem(ModelState);
        }
        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);
        await signInManager.RefreshSignInAsync(user);
        return NoContent();
    }

    [HttpPost("logout")]
    [Authorize(Policy = SystemPolicies.StaffPortal)]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }
}

public sealed record SessionResponse(
    Guid Id,
    string Email,
    string FullName,
    Guid? BranchId,
    Guid? DivisionId,
    string? BranchName,
    string? DivisionName,
    IReadOnlyCollection<string> Roles,
    bool MustChangePassword);
public sealed record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);
