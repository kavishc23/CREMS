using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using CREMS.Api.Services;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class CustomersController(ApplicationDbContext db, CurrentStaffScope staffScope,
    UserManager<ApplicationUser> userManager, IEmailQueue emailQueue) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var query = db.Customers.AsNoTracking();
        if (!scope.IsAdministrator)
            query = query.Where(customer => db.Bookings.Any(booking =>
                booking.CustomerId == customer.Id && booking.BranchId == scope.BranchId));

        var customers = await query
            .OrderBy(customer => customer.Name)
            .Select(customer => new CustomerResponse(
                customer.Id, customer.CustomerNumber, customer.Type, customer.Name,
                customer.Email, customer.Phone, customer.Address, customer.IdentificationNumber,
                customer.IsBlocked, customer.IsActive,
                db.Users.Any(user => user.CustomerId == customer.Id),
                db.Users.Where(user => user.CustomerId == customer.Id).Select(user => user.EmailConfirmed).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return Ok(customers);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(
        SaveCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var customerNumber = request.CustomerNumber.Trim().ToUpperInvariant();
        if (await db.Customers.AnyAsync(customer => customer.CustomerNumber == customerNumber, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.CustomerNumber), "A customer with this number already exists.");
            return ValidationProblem(ModelState);
        }

        var customer = new Customer
        {
            CustomerNumber = customerNumber,
            Type = request.Type,
            Name = request.Name.Trim(),
            Email = Normalize(request.Email)?.ToLowerInvariant(),
            Phone = Normalize(request.Phone),
            Address = Normalize(request.Address),
            IdentificationNumber = Normalize(request.IdentificationNumber),
            IsActive = true,
            IsBlocked = false,
        };
        db.Customers.Add(customer);
        AuditWriter.Record(db, scope, "Customer created", "Customer", customer.Id,
            $"{customer.CustomerNumber} — {customer.Name} was created.", scope.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetAll), ToResponse(customer));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Update(
        Guid id,
        SaveCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FindAsync([id], cancellationToken);
        if (customer is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator &&
            !await db.Bookings.AnyAsync(booking => booking.CustomerId == id &&
                booking.BranchId == scope.BranchId, cancellationToken))) return Forbid();

        var customerNumber = request.CustomerNumber.Trim().ToUpperInvariant();
        if (await db.Customers.AnyAsync(
            other => other.Id != id && other.CustomerNumber == customerNumber, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.CustomerNumber), "A customer with this number already exists.");
            return ValidationProblem(ModelState);
        }

        customer.CustomerNumber = customerNumber;
        customer.Type = request.Type;
        customer.Name = request.Name.Trim();
        customer.Email = Normalize(request.Email)?.ToLowerInvariant();
        customer.Phone = Normalize(request.Phone);
        customer.Address = Normalize(request.Address);
        customer.IdentificationNumber = Normalize(request.IdentificationNumber);
        AuditWriter.Record(db, scope, "Customer updated", "Customer", customer.Id,
            $"{customer.CustomerNumber} details were updated.", scope.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(customer));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<CustomerResponse>> SetStatus(
        Guid id,
        SetCustomerStatusRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FindAsync([id], cancellationToken);
        if (customer is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator &&
            !await db.Bookings.AnyAsync(booking => booking.CustomerId == id &&
                booking.BranchId == scope.BranchId, cancellationToken))) return Forbid();
        customer.IsActive = request.IsActive;
        customer.IsBlocked = request.IsBlocked;
        AuditWriter.Record(db, scope, "Customer status changed", "Customer", customer.Id,
            $"{customer.CustomerNumber} active={request.IsActive}, blocked={request.IsBlocked}.", scope.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(customer));
    }

    [HttpPost("{id:guid}/online-access")]
    public async Task<ActionResult> EnableOnlineAccess(Guid id, CancellationToken token)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, token);
        if (customer is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator &&
            !await db.Bookings.AnyAsync(x => x.CustomerId == id && x.BranchId == scope.BranchId, token))) return Forbid();
        if (!customer.IsActive || customer.IsBlocked) return BadRequest(new { message = "Restore the customer before enabling online access." });
        if (string.IsNullOrWhiteSpace(customer.Email)) return BadRequest(new { message = "Record a valid customer email first." });

        var user = await userManager.Users.FirstOrDefaultAsync(x => x.CustomerId == id, token);
        if (user?.EmailConfirmed == true) return Conflict(new { message = "This customer already has active online access." });
        var emailOwner = await userManager.FindByEmailAsync(customer.Email);
        if (emailOwner is not null && emailOwner.CustomerId != id)
            return Conflict(new { message = "That email is already assigned to another CREMS account." });
        if (user is null)
        {
            user = new ApplicationUser { UserName = customer.Email, Email = customer.Email, FullName = customer.Name,
                CustomerId = customer.Id, IsActive = true, EmailConfirmed = false };
            var create = await userManager.CreateAsync(user);
            if (!create.Succeeded) return BadRequest(new { message = string.Join(" ", create.Errors.Select(x => x.Description)) });
            var role = await userManager.AddToRoleAsync(user, SystemRoles.Customer);
            if (!role.Succeeded) throw new InvalidOperationException("Unable to assign the customer portal role.");
        }

        var previous = await db.CustomerAccountActivations.Where(x => x.UserId == user.Id && x.UsedAt == null).ToListAsync(token);
        foreach (var item in previous) item.UsedAt = DateTimeOffset.UtcNow;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var salt = RandomNumberGenerator.GetBytes(16);
        db.CustomerAccountActivations.Add(new CustomerAccountActivation { UserId = user.Id,
            Salt = Convert.ToBase64String(salt), CodeHash = Hash(code, salt), ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            InvitedByUserId = scope.UserId });
        var html = EmailTemplate.Branded("Activate your Carpenters customer account", $"<p>Carpenters has enabled online access for customer <strong>{System.Net.WebUtility.HtmlEncode(customer.CustomerNumber)}</strong>.</p><p>Your activation code is:</p><p style=\"font-size:32px;letter-spacing:8px;font-weight:bold;background:#f5f5f2;padding:18px;text-align:center\">{code}</p><p>Use this code with {System.Net.WebUtility.HtmlEncode(customer.Email)} on the customer sign-in page. It expires in 24 hours.</p>");
        emailQueue.Queue(db, customer.Email, "Activate your Carpenters customer account", html,
            $"Your Carpenters customer account activation code is {code}. It expires in 24 hours.", "CustomerActivation");
        AuditWriter.Record(db, scope, "Customer online access invited", "Customer", customer.Id,
            $"An online account activation was sent for {customer.CustomerNumber}.", scope.BranchId);
        await db.SaveChangesAsync(token);
        return Accepted(new { message = "Activation instructions have been queued for the customer email." });
    }

    [HttpGet("{id:guid}/activity")]
    public async Task<ActionResult> Activity(Guid id, CancellationToken token)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, token);
        if (customer is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !await db.Bookings.AnyAsync(x => x.CustomerId == id && x.BranchId == scope.BranchId, token))) return Forbid();

        var bookings = await db.Bookings.AsNoTracking().Where(x => x.CustomerId == id && (scope.IsAdministrator || x.BranchId == scope.BranchId))
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.BookingNumber, x.Status, x.CreatedAt, x.ApprovedAt, x.BranchId, branchName = x.Branch!.Name, assetCount = x.Items.Count }).Take(100).ToListAsync(token);
        var bookingIds = bookings.Select(x => x.Id).ToArray();
        var invoices = await db.RentalInvoices.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId)).OrderByDescending(x => x.IssuedAt)
            .Select(x => new { x.Id, x.BookingId, x.InvoiceNumber, x.Status, x.Total, x.AmountPaid, x.BalanceDue, x.IssuedAt }).ToListAsync(token);
        var cases = await db.CustomerCases.AsNoTracking().Where(x => x.CustomerId == id && (scope.IsAdministrator || x.BranchId == scope.BranchId)).OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.CaseNumber, x.Type, x.Priority, x.Subject, x.Status, x.CreatedAt }).Take(100).ToListAsync(token);
        var account = await db.Users.AsNoTracking().Where(x => x.CustomerId == id).Select(x => new { x.Id, x.Email, x.EmailConfirmed, x.IsActive, x.LastLoginAt, x.LastActivityAt, x.LockoutEnd }).FirstOrDefaultAsync(token);
        return Ok(new { customer = new { customer.Id, customer.CustomerNumber, customer.Name, customer.Type, customer.Email, customer.Phone, customer.IsActive, customer.IsBlocked }, account, bookings, invoices, cases,
            summary = new { bookings = bookings.Count, invoices = invoices.Count, totalBilled = invoices.Sum(x => x.Total), outstanding = invoices.Sum(x => x.BalanceDue), openCases = cases.Count(x => x.Status != CaseStatus.Resolved && x.Status != CaseStatus.Closed) } });
    }

    [HttpPost("{id:guid}/security/reset-password")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    public async Task<ActionResult> ResetCustomerPassword(Guid id, CancellationToken token)
    {
        var actorId = userManager.GetUserId(User); if (!Guid.TryParse(actorId, out var actor) || !await db.SecurityEvents.AnyAsync(x => x.UserId == actor && x.Type == SecurityEventType.MfaSucceeded && x.Succeeded && x.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-15), token)) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Recent MFA verification is required." });
        var user = await userManager.Users.FirstOrDefaultAsync(x => x.CustomerId == id, token); if (user is null) return NotFound(new { message = "This customer has no online account." });
        var customer = await db.Customers.FindAsync([id], token); if (customer?.Email is null) return BadRequest(new { message = "The customer has no email address." });
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user); var temporary = $"Crems!{RandomNumberGenerator.GetInt32(100000, 999999)}Aa"; var result = await userManager.ResetPasswordAsync(user, resetToken, temporary); if (!result.Succeeded) return BadRequest(result.Errors);
        user.MustChangePassword = true; await userManager.UpdateSecurityStampAsync(user); emailQueue.Queue(db, customer.Email, "Your CREMS password was reset", EmailTemplate.Branded("Temporary CREMS password", $"<p>Your temporary password is <strong>{temporary}</strong>.</p><p>You must replace it after signing in.</p>"), $"Your temporary CREMS password is {temporary}. Replace it after signing in.", "CustomerPasswordReset");
        db.SecurityEvents.Add(Security(user, SecurityEventType.PasswordResetCompleted, "Customer password reset by administrator")); await db.SaveChangesAsync(token); return Accepted(new { message = "A temporary password has been queued to the customer email." });
    }

    [HttpPost("{id:guid}/security/revoke-sessions")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    public async Task<ActionResult> RevokeCustomerSessions(Guid id, CancellationToken token)
    { var user = await userManager.Users.FirstOrDefaultAsync(x => x.CustomerId == id, token); if (user is null) return NotFound(); await userManager.UpdateSecurityStampAsync(user); var active = await db.UserSessions.Where(x => x.UserId == user.Id && x.EndedAt == null).ToListAsync(token); foreach (var session in active) { session.EndedAt = DateTimeOffset.UtcNow; session.EndReason = "Revoked by administrator"; } db.SecurityEvents.Add(Security(user, SecurityEventType.SessionRevoked, "Customer sessions revoked")); await db.SaveChangesAsync(token); return NoContent(); }

    [HttpPost("{id:guid}/security/{action}")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    public async Task<ActionResult> CustomerSecurityAction(Guid id, string action, CancellationToken token)
    {
        var user = await userManager.Users.FirstOrDefaultAsync(x => x.CustomerId == id, token); if (user is null) return NotFound();
        switch (action.ToLowerInvariant())
        {
            case "lock": await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue); user.AccountStatus = AccountLifecycleStatus.Locked; break;
            case "unlock": await userManager.SetLockoutEndDateAsync(user, null); await userManager.ResetAccessFailedCountAsync(user); user.AccountStatus = AccountLifecycleStatus.Active; break;
            case "verify-email": user.EmailConfirmed = true; break;
            case "disable": user.IsActive = false; user.AccountStatus = AccountLifecycleStatus.Deactivated; user.DeactivatedAt = DateTimeOffset.UtcNow; await userManager.UpdateSecurityStampAsync(user); break;
            case "enable": user.IsActive = true; user.AccountStatus = user.EmailConfirmed ? AccountLifecycleStatus.Active : AccountLifecycleStatus.ActivationPending; user.DeactivatedAt = null; break;
            default: return BadRequest(new { message = "Unsupported customer security action." });
        }
        await userManager.UpdateAsync(user); db.SecurityEvents.Add(Security(user, SecurityEventType.AccessChanged, $"Customer portal action: {action}")); await db.SaveChangesAsync(token); return NoContent();
    }

    private SecurityEvent Security(ApplicationUser user, SecurityEventType type, string detail) => new() { UserId = user.Id, Email = user.Email, Type = type, Succeeded = true, Detail = detail, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent = Request.Headers.UserAgent.ToString() };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id, customer.CustomerNumber, customer.Type, customer.Name,
        customer.Email, customer.Phone, customer.Address, customer.IdentificationNumber,
        customer.IsBlocked, customer.IsActive, false, false);
    private static string Hash(string code, byte[] salt) => Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, 100_000, HashAlgorithmName.SHA256, 32));
}

public sealed record SaveCustomerRequest(
    [Required, MaxLength(50)] string CustomerNumber,
    CustomerType Type,
    [Required, MaxLength(150)] string Name,
    [EmailAddress, MaxLength(254)] string? Email,
    [MaxLength(50)] string? Phone,
    [MaxLength(500)] string? Address,
    [MaxLength(100)] string? IdentificationNumber);

public sealed record SetCustomerStatusRequest(bool IsActive, bool IsBlocked);
public sealed record CustomerResponse(
    Guid Id, string CustomerNumber, CustomerType Type, string Name,
    string? Email, string? Phone, string? Address, string? IdentificationNumber,
    bool IsBlocked, bool IsActive, bool HasOnlineAccount, bool EmailConfirmed);
