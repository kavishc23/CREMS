using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using CREMS.Api.Services;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/customer-account")]
public sealed class CustomerAccountController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IEmailQueue emailQueue) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult> Register(RegisterCustomerRequest request, CancellationToken token)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await userManager.FindByEmailAsync(email) is not null)
            return Conflict(new { message = "An account already uses this email address. Sign in or reset its password." });
        if (await db.Customers.AnyAsync(x => x.Email == email, token))
            return Conflict(new { message = "A customer record already uses this email. Contact Carpenters to verify and activate online access." });

        var customer = new Customer
        {
            CustomerNumber = $"CUS-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            Type = request.Type,
            Name = request.Type == CustomerType.Business ? request.BusinessName!.Trim() : request.FullName.Trim(),
            Email = email,
            Phone = request.Phone.Trim(),
            Address = Clean(request.Address),
            IdentificationNumber = Clean(request.IdentificationNumber),
            IsActive = true,
        };
        var user = new ApplicationUser
        {
            UserName = email, Email = email, FullName = request.FullName.Trim(), Customer = customer,
            IsActive = true, EmailConfirmed = false,
        };
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(nameof(request.Password), error.Description);
            return ValidationProblem(ModelState);
        }
        result = await userManager.AddToRoleAsync(user, SystemRoles.Customer);
        if (!result.Succeeded) throw new InvalidOperationException("Unable to assign the customer portal role.");
        QueueVerification(user);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        await signInManager.SignInAsync(user, isPersistent: false);
        return CreatedAtAction(nameof(Session), new { user.Id });
    }

    [HttpPost("activate")]
    public async Task<ActionResult> Activate(ActivateCustomerAccountRequest request, CancellationToken token)
    {
        var normalized = userManager.NormalizeEmail(request.Email.Trim());
        var user = await userManager.Users.FirstOrDefaultAsync(x => x.NormalizedEmail == normalized && x.CustomerId != null, token);
        if (user is null || !user.IsActive || await userManager.HasPasswordAsync(user)) return InvalidActivation();
        var activation = await db.CustomerAccountActivations.Where(x => x.UserId == user.Id && x.UsedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(token);
        if (activation is null || activation.ExpiresAt < DateTimeOffset.UtcNow || activation.FailedAttempts >= 5) return InvalidActivation();
        var salt = Convert.FromBase64String(activation.Salt);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(request.Code.Trim(), salt)), Convert.FromHexString(activation.CodeHash)))
        { activation.FailedAttempts++; await db.SaveChangesAsync(token); return InvalidActivation(); }
        var password = await userManager.AddPasswordAsync(user, request.Password);
        if (!password.Succeeded) { foreach (var error in password.Errors) ModelState.AddModelError(nameof(request.Password), error.Description); return ValidationProblem(ModelState); }
        activation.UsedAt = DateTimeOffset.UtcNow; user.EmailConfirmed = true; await userManager.UpdateAsync(user); await db.SaveChangesAsync(token);
        await signInManager.SignInAsync(user, false); return NoContent();
    }

    [HttpGet("session")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> Session()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive || !user.CustomerId.HasValue) return Unauthorized();
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == user.CustomerId);
        if (customer is null || !customer.IsActive || customer.IsBlocked) return Forbid();
        return Ok(new { user.Id, user.Email, user.FullName, user.CustomerId, customer.CustomerNumber, customer.Type,
            CustomerName = customer.Name, customer.Phone, customer.Address, user.EmailConfirmed });
    }

    [HttpGet("bookings")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> Bookings(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null) return Unauthorized();
        return Ok(await db.Bookings.AsNoTracking().Where(x => x.CustomerId == user.CustomerId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new
            {
                x.Id, Reference = x.BookingNumber, x.Status, x.CreatedAt, BranchName = x.Branch!.Name,
                Items = x.Items.OrderBy(i => i.StartAt).Select(i => new { i.Asset!.Name, i.StartAt, i.EndAt, i.DailyRate,
                    DivisionName = i.Asset.Division != null ? i.Asset.Division.Name : null }).ToList()
            }).ToListAsync(token));
    }

    [HttpPost("bookings/{bookingId:guid}/cancellation-request")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> RequestCancellation(Guid bookingId, CustomerBookingChangeRequest request, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User); if (user?.CustomerId is null) return Unauthorized();
        var booking = await db.Bookings.FirstOrDefaultAsync(x => x.Id == bookingId && x.CustomerId == user.CustomerId, token);
        if (booking is null) return NotFound();
        if (booking.Status == BookingStatus.Draft)
        { booking.Status = BookingStatus.Cancelled; booking.Notes = Append(booking.Notes, $"Customer cancelled online: {Clean(request.Reason) ?? "No reason supplied"}"); await db.SaveChangesAsync(token); return Ok(new { status = "Cancelled" }); }
        if (booking.Status != BookingStatus.Confirmed) return BadRequest(new { message = "Only pending or confirmed future bookings can be cancelled online." });
        if (!await db.CustomerCases.AnyAsync(x => x.CustomerId == user.CustomerId && x.Status != CaseStatus.Resolved && x.Status != CaseStatus.Closed && x.Subject.Contains(booking.BookingNumber), token))
            db.CustomerCases.Add(new CustomerCase { CaseNumber = Number("CASE"), CustomerId = user.CustomerId.Value, BranchId = booking.BranchId, Type = CaseType.General, Priority = CasePriority.Normal, Subject = $"Cancellation request — {booking.BookingNumber}", Description = Clean(request.Reason) ?? "Customer requested cancellation through the portal.", DueAt = DateTimeOffset.UtcNow.AddHours(4) });
        await db.SaveChangesAsync(token); return Accepted(new { status = "Cancellation requested" });
    }

    [HttpPost("bookings/{bookingId:guid}/extension-request")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> RequestExtension(Guid bookingId, CustomerExtensionRequest request, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User); if (user?.CustomerId is null) return Unauthorized();
        var booking = await db.Bookings.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == bookingId && x.CustomerId == user.CustomerId, token);
        if (booking is null) return NotFound();
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.ConvertedToRental)) return BadRequest(new { message = "Only confirmed or active rentals can be extended." });
        var currentEnd = booking.Items.Max(x => x.EndAt);
        if (request.RequestedEndAt <= currentEnd || request.RequestedEndAt > currentEnd.AddMonths(6)) return BadRequest(new { message = "Choose a later return date within six months of the current return date." });
        var assetIds = booking.Items.Select(x => x.AssetId).ToList();
        var conflict = await db.BookingItems.AnyAsync(x => x.BookingId != booking.Id && assetIds.Contains(x.AssetId) && x.StartAt < request.RequestedEndAt && x.EndAt > currentEnd && (x.Booking!.Status == BookingStatus.Confirmed || x.Booking.Status == BookingStatus.ConvertedToRental), token);
        if (conflict) return Conflict(new { message = "The asset has another booking after your return date. Contact the branch for alternatives." });
        db.CustomerCases.Add(new CustomerCase { CaseNumber = Number("CASE"), CustomerId = user.CustomerId.Value, BranchId = booking.BranchId, Type = CaseType.Enquiry, Priority = booking.Status == BookingStatus.ConvertedToRental ? CasePriority.High : CasePriority.Normal, Subject = $"Extension request — {booking.BookingNumber}", Description = $"Requested new return: {request.RequestedEndAt:u}. {Clean(request.Reason)}", DueAt = DateTimeOffset.UtcNow.AddHours(4) });
        await db.SaveChangesAsync(token); return Accepted(new { status = "Extension requested" });
    }

    [HttpPost("bookings/{bookingId:guid}/incident")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> ReportIncident(Guid bookingId, CustomerIncidentRequest request, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User); if (user?.CustomerId is null) return Unauthorized();
        var booking = await db.Bookings.FirstOrDefaultAsync(x => x.Id == bookingId && x.CustomerId == user.CustomerId, token);
        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.ConvertedToRental) return BadRequest(new { message = "Incidents can only be reported for an active hire." });
        var incident = new RentalIncident { BookingId = booking.Id, IncidentNumber = Number("INC"), Type = request.Type, OccurredAt = request.OccurredAt, Description = request.Description.Trim(), Location = Clean(request.Location), PoliceReference = Clean(request.PoliceReference), EvidenceJson = "[]" };
        db.RentalIncidents.Add(incident); db.CustomerCases.Add(new CustomerCase { CaseNumber = Number("CASE"), CustomerId = user.CustomerId.Value, BranchId = booking.BranchId, Type = CaseType.Breakdown, Priority = CasePriority.Critical, Subject = $"Customer incident — {booking.BookingNumber}", Description = request.Description.Trim(), DueAt = DateTimeOffset.UtcNow.AddMinutes(30) });
        await db.SaveChangesAsync(token); return Accepted(new { incident.IncidentNumber });
    }

    [HttpPost("logout")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> Logout() { await signInManager.SignOutAsync(); return NoContent(); }

    [HttpPost("verification/request")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    [EnableRateLimiting("password-reset")]
    public async Task<ActionResult> RequestVerification(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(user.Email)) return Unauthorized();
        if (user.EmailConfirmed) return Ok(new { message = "Your email address is already verified." });
        var recent = await db.EmailVerificationOtps.AnyAsync(x => x.UserId == user.Id && x.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1), token);
        if (!recent) { QueueVerification(user); await db.SaveChangesAsync(token); }
        return Accepted(new { message = "If another code can be issued, it will be sent shortly." });
    }

    [HttpPost("verification/confirm")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> ConfirmVerification(VerifyEmailRequest request, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (user.EmailConfirmed) return NoContent();
        var challenge = await db.EmailVerificationOtps.Where(x => x.UserId == user.Id && x.UsedAt == null)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(token);
        if (challenge is null || challenge.ExpiresAt < DateTimeOffset.UtcNow || challenge.FailedAttempts >= 5) return InvalidCode();
        var salt = Convert.FromBase64String(challenge.Salt);
        var supplied = Convert.FromHexString(Hash(request.Code.Trim(), salt));
        var stored = Convert.FromHexString(challenge.CodeHash);
        if (!CryptographicOperations.FixedTimeEquals(supplied, stored))
        {
            challenge.FailedAttempts++; await db.SaveChangesAsync(token); return InvalidCode();
        }
        challenge.UsedAt = DateTimeOffset.UtcNow; user.EmailConfirmed = true;
        await userManager.UpdateAsync(user); await db.SaveChangesAsync(token);
        return NoContent();
    }

    private void QueueVerification(ApplicationUser user)
    {
        var active = db.EmailVerificationOtps.Where(x => x.UserId == user.Id && x.UsedAt == null);
        foreach (var item in active) item.UsedAt = DateTimeOffset.UtcNow;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var salt = RandomNumberGenerator.GetBytes(16);
        db.EmailVerificationOtps.Add(new EmailVerificationOtp { UserId = user.Id, Salt = Convert.ToBase64String(salt),
            CodeHash = Hash(code, salt), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), RequestedIp = HttpContext.Connection.RemoteIpAddress?.ToString() });
        var html = EmailTemplate.Branded("Verify your customer account", $"<p>Enter this code in your Carpenters customer account:</p><p style=\"font-size:32px;letter-spacing:8px;font-weight:bold;background:#f5f5f2;padding:18px;text-align:center\">{code}</p><p>This one-time code expires in 10 minutes. If you did not create this account, ignore this message.</p>");
        emailQueue.Queue(db, user.Email!, "Verify your Carpenters customer account", html,
            $"Your verification code is {code}. It expires in 10 minutes.", "EmailVerification");
    }

    private BadRequestObjectResult InvalidCode() => BadRequest(new { message = "The verification code is invalid or expired. Request a new code and try again." });
    private BadRequestObjectResult InvalidActivation() => BadRequest(new { message = "The activation details are invalid or expired. Ask Carpenters staff to send a new invitation." });
    private static string Hash(string code, byte[] salt) => Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, 100_000, HashAlgorithmName.SHA256, 32));
    private static string Append(string? existing, string next) => string.IsNullOrWhiteSpace(existing) ? next : $"{existing}\n{next}";
    private static string Number(string prefix) => $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record VerifyEmailRequest([Required, RegularExpression("^[0-9]{6}$")] string Code);
public sealed record CustomerBookingChangeRequest([MaxLength(1000)] string? Reason);
public sealed record CustomerExtensionRequest(DateTimeOffset RequestedEndAt, [MaxLength(1000)] string? Reason);
public sealed record CustomerIncidentRequest(IncidentType Type, DateTimeOffset OccurredAt, [Required, MaxLength(2000)] string Description, [MaxLength(500)] string? Location, [MaxLength(100)] string? PoliceReference);
public sealed record ActivateCustomerAccountRequest([Required, EmailAddress] string Email,
    [Required, RegularExpression("^[0-9]{6}$")] string Code, [Required, MinLength(10)] string Password);

public sealed record RegisterCustomerRequest(
    [Required, MaxLength(150)] string FullName,
    CustomerType Type,
    [MaxLength(150)] string? BusinessName,
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(50)] string Phone,
    [MaxLength(500)] string? Address,
    [MaxLength(100)] string? IdentificationNumber,
    [Required, MinLength(10)] string Password) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == CustomerType.Business && string.IsNullOrWhiteSpace(BusinessName))
            yield return new ValidationResult("Enter the registered business name.", [nameof(BusinessName)]);
    }
}
