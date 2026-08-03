using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class SessionController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : ControllerBase
{
    [HttpGet("session")]
    [Authorize]
    public async Task<ActionResult<SessionResponse>> GetSession()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(new SessionResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            user.BranchId,
            roles.ToArray()));
    }

    [HttpPost("logout")]
    [Authorize]
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
    IReadOnlyCollection<string> Roles);
