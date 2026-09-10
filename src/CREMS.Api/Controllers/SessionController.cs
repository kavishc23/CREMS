using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CREMS.Api.Services;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class SessionController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    CREMS.Api.Data.ApplicationDbContext db,
    CREMS.Api.Services.WindowSessionRegistry windowSessions,
    IEmailQueue emailQueue) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        // Use the same response for missing and disabled accounts to prevent account discovery.
        if (user is null || !user.IsActive || user.AccountStatus is AccountLifecycleStatus.Suspended or AccountLifecycleStatus.Deactivated) { await Record(null, request.Email, SecurityEventType.LoginFailed, false, "Unavailable account"); return Unauthorized(); }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) { await Record(user.Id, user.Email, SecurityEventType.LoginFailed, false, result.IsLockedOut ? "Account locked" : "Invalid credentials"); return Unauthorized(); }
        var roles = await userManager.GetRolesAsync(user);
        // Temporary project-stage exception: Super Administrators authenticate with their
        // password only while the team is finalising the external email provider.
        // Other accounts still honour their explicit MFA settings, and Administrators
        // continue to require MFA by role.
        var isSuperAdministrator = roles.Contains(SystemRoles.SuperAdministrator);
        var requiresMfa = !isSuperAdministrator &&
            (user.MfaEnabled || user.MfaRequired || roles.Contains(SystemRoles.Administrator));
        if (requiresMfa)
        {
            var challenge=await IssueMfa(user);
            return Accepted(new { requiresMfa = true, challengeId = challenge.Id, maskedDestination = Mask(user.Email!), message="A new verification code has been queued for immediate delivery." });
        }
        await CompleteSignIn(user); return NoContent();
    }

    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> VerifyMfa(MfaVerifyRequest request)
    {
        var challenge = await db.MfaChallenges.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == request.ChallengeId);
        if (challenge?.User is null || challenge.UsedAt != null || challenge.ExpiresAt <= DateTimeOffset.UtcNow || challenge.FailedAttempts >= 5) return Unauthorized();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(challenge.CodeHash), Convert.FromHexString(Hash(request.Code.Trim(), Convert.FromBase64String(challenge.Salt)))))
        { challenge.FailedAttempts++; await Record(challenge.UserId, challenge.User.Email, SecurityEventType.MfaFailed, false, "Invalid MFA code", false); await db.SaveChangesAsync(); return Unauthorized(); }
        challenge.UsedAt = DateTimeOffset.UtcNow; await Record(challenge.UserId, challenge.User.Email, SecurityEventType.MfaSucceeded, true, "Login MFA passed", false); await db.SaveChangesAsync(); await CompleteSignIn(challenge.User); return NoContent();
    }

    [HttpPost("mfa/{challengeId:guid}/resend")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResendMfa(Guid challengeId)
    {
        var previous=await db.MfaChallenges.Include(x=>x.User).FirstOrDefaultAsync(x=>x.Id==challengeId);
        if(previous?.User is null||previous.UsedAt.HasValue)return Accepted(new{message="If the challenge is valid, a new code has been sent."});
        if(previous.CreatedAt>DateTimeOffset.UtcNow.AddSeconds(-30))return StatusCode(StatusCodes.Status429TooManyRequests,new{message="Wait 30 seconds before requesting another code."});
        previous.UsedAt=DateTimeOffset.UtcNow;
        var challenge=await IssueMfa(previous.User);
        return Accepted(new{challengeId=challenge.Id,maskedDestination=Mask(previous.User.Email!),message="A new verification code has been queued."});
    }

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
        var userId = userManager.GetUserId(User);
        if (Guid.TryParse(userId, out var parsed)) { var windowId = Request.Headers["X-CREMS-Window-Id"].ToString(); var sessions = await db.UserSessions.Where(x => x.UserId == parsed && x.WindowId == windowId && x.EndedAt == null).ToListAsync(); foreach (var session in sessions) { session.EndedAt = DateTimeOffset.UtcNow; session.EndReason = "Logout"; } await Record(parsed, User.Identity?.Name, SecurityEventType.Logout, true, null, false); await db.SaveChangesAsync(); }
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        if (!string.IsNullOrWhiteSpace(userId)) windowSessions.End(userId);
        return NoContent();
    }

    [HttpGet("security-history")]
    [Authorize]
    public async Task<ActionResult> SecurityHistory(CancellationToken token)
    { var user = await userManager.GetUserAsync(User); if (user is null) return Unauthorized(); return Ok(new { sessions = await db.UserSessions.AsNoTracking().Where(x => x.UserId == user.Id).OrderByDescending(x => x.CreatedAt).Take(30).ToListAsync(token), events = await db.SecurityEvents.AsNoTracking().Where(x => x.UserId == user.Id).OrderByDescending(x => x.OccurredAt).Take(100).ToListAsync(token) }); }

    [HttpPost("sessions/{id:guid}/revoke")]
    [Authorize]
    public async Task<ActionResult> RevokeOwnSession(Guid id, CancellationToken token)
    { var user = await userManager.GetUserAsync(User); var session = await db.UserSessions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user!.Id, token); if (session is null) return NotFound(); session.EndedAt = DateTimeOffset.UtcNow; session.EndReason = "Revoked by user"; await Record(user!.Id, user.Email, SecurityEventType.SessionRevoked, true, null, false); await db.SaveChangesAsync(token); return NoContent(); }

    private async Task CompleteSignIn(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains(SystemRoles.Customer))
        {
            var principal = await claimsFactory.CreateAsync(user);
            await HttpContext.SignInAsync(SystemAuthenticationSchemes.Customer, principal);
        }
        else
        {
            await signInManager.SignInAsync(user, isPersistent: false);
        }
        var now = DateTimeOffset.UtcNow; user.LastLoginAt = now; user.LastActivityAt = now; user.LastLoginIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var windowId = Request.Headers["X-CREMS-Window-Id"].ToString(); db.UserSessions.Add(new UserSession { UserId = user.Id, WindowId = windowId, IpAddress = user.LastLoginIp, UserAgent = Request.Headers.UserAgent.ToString(), DeviceLabel = Device(Request.Headers.UserAgent.ToString()), ExpiresAt = now.AddMinutes(15) });
        await Record(user.Id, user.Email, SecurityEventType.LoginSucceeded, true, null, false); await db.SaveChangesAsync();
    }
    private async Task<MfaChallenge> IssueMfa(ApplicationUser user)
    {
        var active=await db.MfaChallenges.Where(x=>x.UserId==user.Id&&x.UsedAt==null).ToListAsync();foreach(var old in active)old.UsedAt=DateTimeOffset.UtcNow;
        var code=RandomNumberGenerator.GetInt32(0,1_000_000).ToString("D6",CultureInfo.InvariantCulture);var salt=RandomNumberGenerator.GetBytes(16);
        var challenge=new MfaChallenge{UserId=user.Id,CodeHash=Hash(code,salt),Salt=Convert.ToBase64String(salt),ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10)};
        db.MfaChallenges.Add(challenge);emailQueue.Queue(db,user.Email!,"Your CREMS verification code",EmailTemplate.Branded("Verify your CREMS sign-in",$"<p>Your verification code is:</p><p style=\"font-size:32px;font-weight:bold;letter-spacing:8px\">{code}</p><p>It expires in 10 minutes. If you did not attempt to sign in, contact your administrator.</p>"),$"Your CREMS verification code is {code}. It expires in 10 minutes.","Mfa");
        await Record(user.Id,user.Email,SecurityEventType.MfaChallengeIssued,true,"Email login challenge",false);await db.SaveChangesAsync();return challenge;
    }
    private async Task Record(Guid? userId, string? email, SecurityEventType type, bool succeeded, string? detail, bool save = true) { db.SecurityEvents.Add(new SecurityEvent { UserId = userId, Email = email, Type = type, Succeeded = succeeded, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent = Request.Headers.UserAgent.ToString(), WindowId = Request.Headers["X-CREMS-Window-Id"].ToString(), Detail = detail }); if (save) await db.SaveChangesAsync(); }
    private static string Hash(string value, byte[] salt) => Convert.ToHexString(SHA256.HashData(salt.Concat(Encoding.UTF8.GetBytes(value)).ToArray()));
    private static string Mask(string email) { var parts = email.Split('@'); return parts.Length == 2 ? $"{parts[0][0]}***@{parts[1]}" : "your email"; }
    private static string Device(string ua) => ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ? "Mobile browser" : "Desktop browser";
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
public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public sealed record MfaVerifyRequest(Guid ChallengeId, [Required] string Code);
