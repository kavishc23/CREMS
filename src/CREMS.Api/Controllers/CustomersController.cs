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
    UserManager<ApplicationUser> userManager, IEmailQueue emailQueue, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && scope.BranchIds.Count == 0)) return Forbid();
        var query = db.Customers.AsNoTracking();
        if (!scope.IsAdministrator)
            query = query.Where(customer => db.Bookings.Any(booking =>
                booking.CustomerId == customer.Id && scope.BranchIds.Contains(booking.BranchId) && booking.Items.Any() &&
                booking.Items.All(i => i.Asset != null && i.Asset.DivisionId.HasValue && scope.DivisionIds.Contains(i.Asset.DivisionId.Value))));

        var customers = await query
            .OrderBy(customer => customer.Name)
            .Select(customer => new CustomerResponse(
                customer.Id, customer.CustomerNumber, customer.Name,
                customer.Email, customer.Phone, customer.Address, customer.IdentificationNumber, customer.HirePreferences,
                customer.IsBlocked, customer.IsActive,
                db.Users.Any(user => user.CustomerId == customer.Id),
                db.Users.Where(user => user.CustomerId == customer.Id).Select(user => user.EmailConfirmed).FirstOrDefault(),
                db.DocumentRecords.Where(document => document.EntityType == nameof(Customer) &&
                    document.EntityId == customer.Id && document.Type == "DriverLicence")
                    .OrderByDescending(document => document.CreatedAt).Select(document => (Guid?)document.Id).FirstOrDefault(),
                db.DocumentRecords.Where(document => document.EntityType == nameof(Customer) &&
                    document.EntityId == customer.Id && document.Type == "DriverLicence")
                    .OrderByDescending(document => document.CreatedAt).Select(document => document.FileName).FirstOrDefault(),
                db.CustomerLicences.Where(x => x.CustomerId == customer.Id && x.Status == LicenceVerificationStatus.Verified).OrderByDescending(x => x.ConfirmedAt).Select(x => x.LicenceNumber).FirstOrDefault(),
                db.CustomerLicences.Where(x => x.CustomerId == customer.Id && x.Status == LicenceVerificationStatus.Verified).OrderByDescending(x => x.ConfirmedAt).Select(x => x.LicenceClasses).FirstOrDefault(),
                db.CustomerLicences.Any(x => x.CustomerId == customer.Id && x.Status == LicenceVerificationStatus.Verified) ? "Verified" : "Required"))
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
            Type = CustomerType.Individual,
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
        customer.Type = CustomerType.Individual;
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
            Salt = Convert.ToBase64String(salt), CodeHash = Hash(code, salt), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            InvitedByUserId = scope.UserId });
        var html = EmailTemplate.Branded("Activate your Carpenters customer account", $"<p>Carpenters has enabled online access for customer <strong>{System.Net.WebUtility.HtmlEncode(customer.CustomerNumber)}</strong>.</p><p>Your activation code is:</p><p style=\"font-size:32px;letter-spacing:8px;font-weight:bold;background:#f5f5f2;padding:18px;text-align:center\">{code}</p><p>Use this code with {System.Net.WebUtility.HtmlEncode(customer.Email)} on the customer sign-in page. It expires in 10 minutes.</p>");
        emailQueue.Queue(db, customer.Email, "Activate your Carpenters customer account", html,
            $"Your Carpenters customer account activation code is {code}. It expires in 10 minutes.", "CustomerActivation");
        AuditWriter.Record(db, scope, "Customer online access invited", "Customer", customer.Id,
            $"An online account activation was sent for {customer.CustomerNumber}.", scope.BranchId);
        await db.SaveChangesAsync(token);
        return Accepted(new { message = "Activation instructions have been queued for the customer email." });
    }

    [HttpGet("{id:guid}/activity")]
    public async Task<ActionResult> Activity(Guid id, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken token = default)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, token);
        if (customer is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !await db.Bookings.AnyAsync(x => x.CustomerId == id && x.BranchId == scope.BranchId, token))) return Forbid();

        pageSize = Math.Clamp(pageSize, 4, 10); page = Math.Max(1, page); var term = search?.Trim();
        var bookingQuery = db.Bookings.AsNoTracking().Where(x => x.CustomerId == id && (scope.IsAdministrator || x.BranchId == scope.BranchId));
        var quoteQuery = db.SalesQuotes.AsNoTracking().Where(x => x.CustomerId == id && (scope.IsAdministrator || x.BranchId == scope.BranchId));
        var invoiceQuery = db.RentalInvoices.AsNoTracking().Where(x => x.Booking!.CustomerId == id && (scope.IsAdministrator || x.Booking.BranchId == scope.BranchId));
        if (!string.IsNullOrWhiteSpace(term)) { bookingQuery = bookingQuery.Where(x => x.BookingNumber.Contains(term) || x.Branch!.Name.Contains(term)); quoteQuery = quoteQuery.Where(x => x.QuoteNumber.Contains(term) || db.Branches.Any(branch => branch.Id == x.BranchId && branch.Name.Contains(term))); invoiceQuery = invoiceQuery.Where(x => x.InvoiceNumber.Contains(term) || x.Booking!.Branch!.Name.Contains(term)); }
        var bookingCount = await bookingQuery.CountAsync(token); var quoteCount = await quoteQuery.CountAsync(token); var invoiceCount = await invoiceQuery.CountAsync(token);
        var bookings = await bookingQuery.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new { x.Id, x.BookingNumber, x.Status, x.CreatedAt, x.ApprovedAt, x.BranchId, branchName = x.Branch!.Name, assetCount = x.Items.Count }).ToListAsync(token);
        var quotations = await quoteQuery.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new { x.Id, x.QuoteNumber, x.Status, x.Total, x.ValidUntil, x.CreatedAt, branchName = db.Branches.Where(branch => branch.Id == x.BranchId).Select(branch => branch.Name).FirstOrDefault()! }).ToListAsync(token);
        var invoices = await invoiceQuery.OrderByDescending(x => x.IssuedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new { x.Id, x.BookingId, x.InvoiceNumber, x.Status, x.Total, x.AmountPaid, x.BalanceDue, x.IssuedAt }).ToListAsync(token);
        var totals = await db.RentalInvoices.AsNoTracking().Where(x => x.Booking!.CustomerId == id && (scope.IsAdministrator || x.Booking.BranchId == scope.BranchId)).GroupBy(_ => 1).Select(x => new { totalBilled = x.Sum(y => y.Total), outstanding = x.Sum(y => y.BalanceDue) }).FirstOrDefaultAsync(token);
        var licence = await db.CustomerLicences.AsNoTracking().Where(x => x.CustomerId == id && x.Status == LicenceVerificationStatus.Verified).OrderByDescending(x => x.ConfirmedAt).Select(x => new { x.LicenceNumber, x.LicenceClasses, x.Status, x.UpdatedAt }).FirstOrDefaultAsync(token);
        var account = await db.Users.AsNoTracking().Where(x => x.CustomerId == id).Select(x => new { x.Id, x.Email, x.EmailConfirmed, x.IsActive, x.LastLoginAt, x.LastActivityAt, x.LockoutEnd }).FirstOrDefaultAsync(token);
        return Ok(new { customer = new { customer.Id, customer.CustomerNumber, customer.Name, customer.Email, customer.Phone, customer.Address, customer.HirePreferences, customer.IsActive, customer.IsBlocked }, account, licence, bookings, quotations, invoices,
            pagination = new { page, pageSize, bookings = bookingCount, quotations = quoteCount, invoices = invoiceCount },
            summary = new { bookings = bookingCount, quotations = quoteCount, invoices = invoiceCount, totalBilled = totals?.totalBilled ?? 0, outstanding = totals?.outstanding ?? 0 } });
    }

    [HttpPost("{id:guid}/security/reset-password")]
    [Authorize(Policy = SystemPermissions.CustomersManageAccess)]
    public async Task<ActionResult> ResetCustomerPassword(Guid id, CancellationToken token)
    {
        var actorId = userManager.GetUserId(User); if (!Guid.TryParse(actorId, out var actor)) return Forbid();
        var mfaRequired = await db.Users.Where(user => user.Id == actor).Select(user => user.MfaRequired).FirstOrDefaultAsync(token);
        if (mfaRequired && !await db.SecurityEvents.AnyAsync(x => x.UserId == actor && x.Type == SecurityEventType.MfaSucceeded && x.Succeeded && x.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-15), token)) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Recent MFA verification is required." });
        var user = await userManager.Users.FirstOrDefaultAsync(x => x.CustomerId == id, token); if (user is null) return NotFound(new { message = "This customer has no online account." });
        var customer = await db.Customers.FindAsync([id], token); if (customer?.Email is null) return BadRequest(new { message = "The customer has no email address." });
        var active = await db.PasswordResetOtps.Where(x => x.UserId == user.Id && x.UsedAt == null).ToListAsync(token); foreach (var item in active) item.UsedAt = DateTimeOffset.UtcNow;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture); var salt = RandomNumberGenerator.GetBytes(16);
        db.PasswordResetOtps.Add(new PasswordResetOtp { UserId = user.Id, Salt = Convert.ToBase64String(salt), CodeHash = Hash(code, salt), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), RequestedIp = HttpContext.Connection.RemoteIpAddress?.ToString() });
        emailQueue.Queue(db, customer.Email, "Reset your CREMS password", EmailTemplate.Branded("Reset your CREMS password", $"<p>An administrator requested password-reset instructions for your CREMS account.</p><p style=\"font-size:32px;letter-spacing:8px;font-weight:bold\">{code}</p><p>This code expires in 10 minutes.</p>"), $"Your CREMS password reset code is {code}. It expires in 10 minutes.", "CustomerPasswordReset");
        db.SecurityEvents.Add(Security(user, SecurityEventType.PasswordResetRequested, "Customer password-reset instructions sent by administrator")); await db.SaveChangesAsync(token); return Accepted(new { message = "Password-reset instructions have been queued to the registered email." });
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

    [HttpPost("{id:guid}/driver-licence")]
    [RequestSizeLimit(5_500_000)]
    public async Task<ActionResult> UploadDriverLicence(Guid id, [FromForm] IFormFile file, CancellationToken token)
    {
        var customer = await db.Customers.FindAsync([id], token);
        if (customer is null) return NotFound();
        if (!await CanAccessCustomer(id, token)) return Forbid();
        var validation = ValidateDocument(file);
        if (validation is not null) return BadRequest(new { message = validation });

        await using var content = new MemoryStream();
        await file.CopyToAsync(content, token);
        var bytes = content.ToArray();
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!HasValidSignature(extension, bytes)) return BadRequest(new { message = "The file content does not match its extension." });

        var root = Path.Combine(environment.ContentRootPath, "App_Data", "customer-documents", id.ToString("N"));
        Directory.CreateDirectory(root);
        var storageName = $"{Guid.NewGuid():N}{extension}";
        await System.IO.File.WriteAllBytesAsync(Path.Combine(root, storageName), bytes, token);
        var previous = await db.DocumentRecords.Where(document => document.EntityType == nameof(Customer) &&
            document.EntityId == id && document.Type == "DriverLicence").ToListAsync(token);
        foreach (var old in previous)
        {
            var oldPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "customer-documents", old.StoragePath));
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        }
        db.DocumentRecords.RemoveRange(previous);
        var record = new DocumentRecord { DocumentNumber = $"DOC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            EntityType = nameof(Customer), EntityId = id, Type = "DriverLicence", FileName = Path.GetFileName(file.FileName),
            StoragePath = Path.Combine(id.ToString("N"), storageName), ContentHash = Convert.ToHexString(SHA256.HashData(bytes)) };
        db.DocumentRecords.Add(record);
        await db.SaveChangesAsync(token);
        return Ok(new { record.Id, record.FileName });
    }

    [HttpGet("{id:guid}/driver-licence/{documentId:guid}")]
    public async Task<ActionResult> DownloadDriverLicence(Guid id, Guid documentId, CancellationToken token)
    {
        if (!await CanAccessCustomer(id, token)) return Forbid();
        var record = await db.DocumentRecords.AsNoTracking().FirstOrDefaultAsync(document => document.Id == documentId &&
            document.EntityType == nameof(Customer) && document.EntityId == id && document.Type == "DriverLicence", token);
        if (record is null) return NotFound();
        var storageRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "customer-documents"));
        var path = Path.GetFullPath(Path.Combine(storageRoot, record.StoragePath));
        if (!path.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !System.IO.File.Exists(path)) return NotFound();
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch { ".pdf" => "application/pdf", ".png" => "image/png", _ => "image/jpeg" };
        return PhysicalFile(path, contentType, record.FileName);
    }

    private async Task<bool> CanAccessCustomer(Guid id, CancellationToken token)
    {
        var scope = await staffScope.GetAsync(User);
        return scope is not null && (scope.IsAdministrator || await db.Bookings.AnyAsync(booking =>
            booking.CustomerId == id && booking.BranchId == scope.BranchId, token));
    }

    private static string? ValidateDocument(IFormFile file)
    {
        if (file.Length is <= 0 or > 5_242_880) return "Choose a PDF, JPEG or PNG file no larger than 5 MB.";
        return Path.GetExtension(file.FileName).ToLowerInvariant() is ".pdf" or ".jpg" or ".jpeg" or ".png"
            ? null : "Only PDF, JPEG and PNG files are accepted.";
    }

    private static bool HasValidSignature(string extension, byte[] bytes) => extension switch
    {
        ".pdf" => bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46,
        ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        ".png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        _ => false,
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id, customer.CustomerNumber, customer.Name,
        customer.Email, customer.Phone, customer.Address, customer.IdentificationNumber, customer.HirePreferences,
        customer.IsBlocked, customer.IsActive, false, false, null, null, null, null, "Required");
    private static string Hash(string code, byte[] salt) => Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, 100_000, HashAlgorithmName.SHA256, 32));
}

public sealed record SaveCustomerRequest(
    [Required, RegularExpression("^CUS-[0-9]{6}$", ErrorMessage = "Use the customer number format CUS-000001."), MaxLength(50)] string CustomerNumber,
    [Required, MaxLength(150)] string Name,
    [EmailAddress, MaxLength(254)] string? Email,
    [MaxLength(50)] string? Phone,
    [MaxLength(500)] string? Address,
    [MaxLength(100)] string? IdentificationNumber);

public sealed record SetCustomerStatusRequest(bool IsActive, bool IsBlocked);
public sealed record CustomerResponse(
    Guid Id, string CustomerNumber, string Name,
    string? Email, string? Phone, string? Address, string? IdentificationNumber, IReadOnlyList<CustomerHirePreference> HirePreferences,
    bool IsBlocked, bool IsActive, bool HasOnlineAccount, bool EmailConfirmed,
    Guid? DriverLicenceDocumentId, string? DriverLicenceFileName,
    string? LicenceNumber, string? LicenceClasses, string LicenceStatus);
