using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/auth/password-reset")]
[EnableRateLimiting("password-reset")]
public sealed class AccountRecoveryController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailQueue emailQueue) : ControllerBase
{
    [HttpPost("request")]
    public async Task<ActionResult> RequestCode(PasswordResetCodeRequest request, CancellationToken token)
    {
        var normalized = userManager.NormalizeEmail(request.Email.Trim());
        var user = await userManager.Users.FirstOrDefaultAsync(x => x.NormalizedEmail == normalized, token);
        if (user is not null && user.IsActive && !string.IsNullOrWhiteSpace(user.Email))
        {
            var recent = await db.PasswordResetOtps.AnyAsync(x => x.UserId == user.Id && x.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1), token);
            if (!recent)
            {
                var active = await db.PasswordResetOtps.Where(x => x.UserId == user.Id && x.UsedAt == null).ToListAsync(token);
                foreach (var item in active) item.UsedAt = DateTimeOffset.UtcNow;
                var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
                var salt = RandomNumberGenerator.GetBytes(16);
                db.PasswordResetOtps.Add(new PasswordResetOtp { UserId = user.Id, Salt = Convert.ToBase64String(salt), CodeHash = Hash(code, salt), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), RequestedIp = HttpContext.Connection.RemoteIpAddress?.ToString() });
                var html = EmailTemplate.Branded("Reset your CREMS password", $"<p>A password reset was requested for your staff account.</p><p style=\"font-size:32px;letter-spacing:8px;font-weight:bold;background:#f5f5f2;padding:18px;text-align:center\">{code}</p><p>This code expires in 10 minutes and can be used once. If you did not request this, ignore this email and contact your administrator if you are concerned.</p>");
                emailQueue.Queue(db, user.Email, "Your CREMS password reset code", html, $"Your CREMS password reset code is {code}. It expires in 10 minutes.", "PasswordReset");
                await db.SaveChangesAsync(token);
            }
        }
        return Accepted(new { message = "If an active account matches that address, a reset code will be sent shortly." });
    }

    [HttpPost("complete")]
    public async Task<ActionResult> Complete(CompletePasswordResetRequest request, CancellationToken token)
    {
        var normalized = userManager.NormalizeEmail(request.Email.Trim());
        var user = await userManager.Users.FirstOrDefaultAsync(x => x.NormalizedEmail == normalized, token);
        if (user is null || !user.IsActive) return Invalid();
        var challenge = await db.PasswordResetOtps.Where(x => x.UserId == user.Id && x.UsedAt == null).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(token);
        if (challenge is null || challenge.ExpiresAt < DateTimeOffset.UtcNow || challenge.FailedAttempts >= 5) return Invalid();
        var salt = Convert.FromBase64String(challenge.Salt); var supplied = Convert.FromHexString(Hash(request.Code.Trim(), salt)); var stored = Convert.FromHexString(challenge.CodeHash);
        if (!CryptographicOperations.FixedTimeEquals(supplied, stored)) { challenge.FailedAttempts++; await db.SaveChangesAsync(token); return Invalid(); }
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user); var result = await userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);
        if (!result.Succeeded) { foreach (var error in result.Errors) ModelState.AddModelError(nameof(request.NewPassword), error.Description); return ValidationProblem(ModelState); }
        challenge.UsedAt = DateTimeOffset.UtcNow; user.MustChangePassword = false; user.PasswordChangedAt = DateTimeOffset.UtcNow; await userManager.UpdateSecurityStampAsync(user); await db.SaveChangesAsync(token);
        if (!string.IsNullOrWhiteSpace(user.Email)) { var html = EmailTemplate.Branded("Your CREMS password was changed", "<p>Your staff account password has been reset successfully.</p><p>If you did not make this change, contact your CREMS administrator immediately.</p>"); emailQueue.Queue(db, user.Email, "CREMS password changed", html, "Your CREMS password was changed. Contact your administrator immediately if this was not you.", "Security"); await db.SaveChangesAsync(token); }
        return NoContent();
    }

    private BadRequestObjectResult Invalid() => BadRequest(new { message = "The reset code is invalid or has expired. Request a new code and try again." });
    private static string Hash(string code, byte[] salt) => Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, 100_000, HashAlgorithmName.SHA256, 32));
}

public sealed record PasswordResetCodeRequest([Required, EmailAddress] string Email);
public sealed record CompletePasswordResetRequest([Required, EmailAddress] string Email, [Required, RegularExpression("^[0-9]{6}$")] string Code, [Required, MinLength(10)] string NewPassword);
