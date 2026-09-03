using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Common;
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
using System.Text.Json;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/customer-account")]
public sealed class CustomerAccountController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IEmailQueue emailQueue,
    IWebHostEnvironment environment) : ControllerBase
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
            CustomerNumber = await NextCustomerNumber(token),
            Type = CustomerType.Individual,
            Name = request.FullName.Trim(),
            Email = email,
            Phone = request.Phone.Trim(),
            Address = Clean(request.Address),
            IdentificationNumber = Clean(request.IdentificationNumber),
            HirePreferences = request.HirePreferences.Distinct().ToList(),
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
        return Ok(new { user.Id, user.Email, user.FullName, user.CustomerId, customer.CustomerNumber,
            CustomerName = customer.Name, customer.Phone, customer.Address, customer.IdentificationNumber,
            customer.HirePreferences, user.EmailConfirmed });
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

    [HttpGet("bookings/{bookingId:guid}")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> BookingDetails(Guid bookingId, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null) return Unauthorized();
        var booking = await db.Bookings.AsNoTracking()
            .Include(x => x.Branch).Include(x => x.Items).ThenInclude(x => x.Asset).ThenInclude(x => x!.Division)
            .Include(x => x.Items).ThenInclude(x => x.Asset).ThenInclude(x => x!.ServiceOffering)
            .Include(x => x.Charges).Include(x => x.Payments).Include(x => x.Inspections)
            .Include(x => x.Incidents).Include(x => x.Invoice).ThenInclude(x => x!.Lines)
            .Include(x => x.RentalAgreement).ThenInclude(x => x!.Addendums)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.CustomerId == user.CustomerId, token);
        if (booking is null) return NotFound();

        var dispatches = await db.DispatchJobs.AsNoTracking().Where(x => x.BookingId == booking.Id)
            .OrderBy(x => x.ScheduledAt).Select(x => new { x.DispatchNumber, x.Type, x.Status, x.ScheduledAt,
                x.Address, x.AssignedDriver, x.DeliveryCharge, x.CompletedAt }).ToListAsync(token);
        var documents = await db.DocumentRecords.AsNoTracking()
            .Where(x => x.EntityType == nameof(Booking) && x.EntityId == booking.Id ||
                        x.EntityType == nameof(Customer) && x.EntityId == user.CustomerId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.DocumentNumber, x.Type, x.FileName,
                x.ExpiresOn, x.CreatedAt }).ToListAsync(token);
        var cases = await db.CustomerCases.AsNoTracking().Where(x => x.CustomerId == user.CustomerId &&
                x.Subject.Contains(booking.BookingNumber))
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.CaseNumber, x.Type, x.Status,
                x.Subject, x.CreatedAt }).ToListAsync(token);
        var operatingHours = await db.BranchOperatingPeriods.AsNoTracking().Where(x => x.BranchId == booking.BranchId)
            .OrderBy(x => x.DayOfWeek).Select(x => new { x.DayOfWeek, x.OpensAt, x.ClosesAt, x.IsClosed,
                x.PickupCutoff, x.ReturnCutoff }).ToListAsync(token);

        var identificationVerified = booking.Inspections.Any(x => x.IdentificationVerified) ||
            documents.Any(x => x.Type.Contains("ident", StringComparison.OrdinalIgnoreCase));
        var professionalDriverProvided = booking.Charges.Any(x => x.Category == ChargeCategory.Driver);
        var licenceRequired = booking.Items.Any(x => x.Asset!.Type == Domain.Assets.AssetType.Vehicle) && !professionalDriverProvided;
        var licenceVerified = !licenceRequired || booking.Inspections.Any(x => x.DriverLicenceVerified) ||
            documents.Any(x => x.Type.Contains("licence", StringComparison.OrdinalIgnoreCase) || x.Type.Contains("license", StringComparison.OrdinalIgnoreCase));
        var depositPaid = booking.DepositRequired <= 0 || booking.Payments
            .Where(x => x.Status == PaymentStatus.Recorded && x.Type == PaymentType.Deposit).Sum(x => x.Amount) >= booking.DepositRequired;
        var agreementReady = booking.RentalAgreement is not null && booking.RentalAgreement.Status != AgreementStatus.Draft;
        var preHireComplete = booking.Inspections.Any(x => x.Type == InspectionType.Handover);
        var personnelRequired = booking.Items.Any(x => x.Asset!.PersonnelRequirement == Domain.Common.PersonnelRequirement.Required) ||
            booking.Charges.Any(x => x.Category is ChargeCategory.Driver or ChargeCategory.Operator);
        var personnelReady = !personnelRequired || await db.BookingPersonnelAssignments.AsNoTracking()
            .AnyAsync(x => x.BookingId == booking.Id && x.Status != Domain.Operations.AssignmentStatus.Cancelled, token);
        var deliveryRequested = dispatches.Any(x => x.Type == DispatchType.Delivery);
        var deliveryReady = !deliveryRequested || dispatches.Any(x => x.Type == DispatchType.Delivery &&
            x.Status is DispatchStatus.Assigned or DispatchStatus.EnRoute or DispatchStatus.Arrived or DispatchStatus.Completed);
        var requiredDocuments = booking.Items.SelectMany(x => ParseStringArray(x.Asset?.ServiceOffering?.RequiredDocumentsJson))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var requiredDocumentsReady = requiredDocuments.Count == 0 || requiredDocuments.All(required => documents.Any(document =>
            document.Type.Contains(required, StringComparison.OrdinalIgnoreCase) || document.FileName.Contains(required, StringComparison.OrdinalIgnoreCase)));

        var baseSubtotal = booking.Items.Sum(item =>
            Math.Max(1, (decimal)Math.Ceiling((item.EndAt - item.StartAt).TotalDays)) * item.DailyRate);
        var visibleCharges = booking.Charges.Select(x => new { x.Description, x.Category, x.Unit,
            x.Quantity, x.UnitRate, Amount = x.Quantity * x.UnitRate, x.IsTaxable }).ToList();
        var chargeTotal = visibleCharges.Sum(x => x.Amount);
        var taxable = Math.Max(0, baseSubtotal - booking.DiscountAmount + chargeTotal);
        var tax = decimal.Round(taxable * booking.TaxRate / 100m, 2);

        return Ok(new
        {
            booking.Id, Reference = booking.BookingNumber, booking.Status, booking.CreatedAt, booking.UpdatedAt,
            Branch = new { booking.BranchId, booking.Branch!.Name, booking.Branch.Address, booking.Branch.Phone,
                booking.Branch.Email, booking.Branch.PickupInstructions, OperatingHours = operatingHours },
            Items = booking.Items.OrderBy(x => x.StartAt).Select(x => new
            {
                x.AssetId, x.Asset!.Name, x.Asset.Type, x.Asset.Category, x.StartAt, x.EndAt, x.DailyRate,
                DivisionName = x.Asset.Division != null ? x.Asset.Division.Name : null,
                x.Asset.PersonnelRequirement,
            }),
            Pricing = new { BaseSubtotal = baseSubtotal, booking.DiscountAmount, Charges = visibleCharges,
                ChargeTotal = chargeTotal, booking.TaxRate, TaxAmount = tax, Total = taxable + tax,
                booking.DepositRequired },
            Agreement = booking.RentalAgreement is null ? null : new
            {
                booking.RentalAgreement.AgreementNumber, booking.RentalAgreement.Status,
                booking.RentalAgreement.TermsVersion, Terms = ParseJson(booking.RentalAgreement.TermsJson),
                booking.RentalAgreement.CustomerSignatureName, booking.RentalAgreement.CustomerSignedAt,
                booking.RentalAgreement.ApprovedByName, booking.RentalAgreement.ApprovedAt,
                Addendums = booking.RentalAgreement.Addendums.OrderBy(x => x.CreatedAt).Select(x => new
                    { x.AddendumNumber, x.Reason, x.CustomerSignedAt })
            },
            Invoice = booking.Invoice is null ? null : new
            {
                booking.Invoice.InvoiceNumber, booking.Invoice.Status, booking.Invoice.IssuedAt,
                booking.Invoice.Subtotal, booking.Invoice.TaxAmount, booking.Invoice.Total,
                booking.Invoice.AmountPaid, booking.Invoice.BalanceDue,
                Lines = booking.Invoice.Lines.OrderBy(x => x.CreatedAt).Select(x => new
                    { x.Description, x.Quantity, x.UnitPrice, x.TaxRate, x.IsTaxable })
            },
            Payments = booking.Payments.OrderByDescending(x => x.CreatedAt).Select(x => new
                { x.ReceiptNumber, x.Type, x.Method, x.Amount, x.Status, x.CreatedAt }),
            Inspections = booking.Inspections.OrderBy(x => x.CompletedAt).Select(x => new
                { x.Type, x.MeterReading, x.FuelLevelPercent, x.ConditionNotes, x.DamageNotes, x.CompletedAt }),
            Incidents = booking.Incidents.OrderByDescending(x => x.OccurredAt).Select(x => new
                { x.IncidentNumber, x.Type, x.Status, x.OccurredAt, x.Description, x.Location, x.PoliceReference }),
            Readiness = new
            {
                Identification = new { Complete = identificationVerified, Label = "Identification verified" },
                DriverLicence = new { Complete = licenceVerified, Required = licenceRequired, Label = "Driver licence verified" },
                Deposit = new { Complete = depositPaid, RequiredAmount = booking.DepositRequired, Label = "Deposit or payment completed" },
                Agreement = new { Complete = agreementReady, Label = "Agreement ready for pickup" },
                PreHireInspection = new { Complete = preHireComplete, Label = "Pre-hire inspection completed" },
                Personnel = new { Complete = personnelReady, Required = personnelRequired, Label = "Operator or driver confirmed" },
                Delivery = new { Complete = deliveryReady, Required = deliveryRequested, Label = "Delivery confirmed" },
                RequiredDocuments = new { Complete = requiredDocumentsReady, Required = requiredDocuments.Count > 0,
                    Label = requiredDocuments.Count > 0 ? $"Required documents: {string.Join(", ", requiredDocuments)}" : "No additional safety documents required" },
            },
            dispatches, documents, cases,
        });
    }

    [HttpGet("quotes")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> Quotes(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null) return Unauthorized();
        var quotes = await db.SalesQuotes.AsNoTracking().Where(x => x.CustomerId == user.CustomerId)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(token);
        var branchIds = quotes.Select(x => x.BranchId).Distinct().ToList();
        var divisionIds = quotes.Where(x => x.DivisionId.HasValue).Select(x => x.DivisionId!.Value).Distinct().ToList();
        var branches = await db.Branches.AsNoTracking().Where(x => branchIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, token);
        var divisions = await db.Divisions.AsNoTracking().Where(x => divisionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, token);
        return Ok(quotes.Select(x => new
        {
            x.Id, x.QuoteNumber, x.Status, x.ValidUntil, x.JobSite, x.PurchaseOrderNumber,
            x.Subtotal, x.Discount, x.Tax, x.Total, x.Version, x.CreatedAt, x.UpdatedAt,
            BranchName = branches.GetValueOrDefault(x.BranchId),
            DivisionName = x.DivisionId.HasValue ? divisions.GetValueOrDefault(x.DivisionId.Value) : null,
            Lines = ParseJson(x.LineItemsJson), x.ConvertedBookingId,
        }));
    }

    [HttpPost("quotes/{quoteId:guid}/decision")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> DecideQuote(Guid quoteId, CustomerQuoteDecisionRequest request, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null) return Unauthorized();
        var quote = await db.SalesQuotes.FirstOrDefaultAsync(x => x.Id == quoteId && x.CustomerId == user.CustomerId, token);
        if (quote is null) return NotFound();
        if (quote.ValidUntil < DateTimeOffset.UtcNow)
        {
            quote.Status = QuoteStatus.Expired;
            await db.SaveChangesAsync(token);
            return Conflict(new { message = "This quotation has expired. Ask the branch for a revised quotation." });
        }
        var next = request.Accepted ? QuoteStatus.Accepted : QuoteStatus.Rejected;
        if (!QuotePolicy.IsValidTransition(quote.Status, next))
            return Conflict(new { message = $"A {quote.Status} quotation cannot be {next.ToString().ToLowerInvariant()}." });
        if (!request.Accepted && string.IsNullOrWhiteSpace(request.Note))
            return BadRequest(new { message = "Please tell us why you are declining the quotation." });
        quote.Status = next;
        quote.LostReason = request.Accepted ? null : Clean(request.Note);
        quote.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        return Ok(new { quote.Id, quote.QuoteNumber, quote.Status, quote.UpdatedAt });
    }

    [HttpGet("corporate-account")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> CorporateAccount(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null) return Unauthorized();
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == user.CustomerId, token);
        if (customer is null) return NotFound();
        if (customer.Type != CustomerType.Business) return NoContent();
        var account = await db.CorporateAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.CustomerId == customer.Id, token);
        var financials = await db.RentalInvoices.AsNoTracking().Where(x => x.Booking!.CustomerId == customer.Id)
            .GroupBy(_ => 1).Select(group => new { Invoiced = group.Sum(x => x.Total), Paid = group.Sum(x => x.AmountPaid),
                Outstanding = group.Sum(x => x.BalanceDue) }).FirstOrDefaultAsync(token);
        return Ok(new
        {
            customer.Name, customer.CustomerNumber,
            LegalName = account?.LegalName ?? customer.Name,
            account?.TaxIdentificationNumber, account?.CreditLimit, account?.PaymentTermsDays,
            account?.PurchaseOrderRequired, account?.CreditHold,
            BillingContact = ParseJson(account?.BillingContactJson),
            AuthorizedContacts = ParseJson(account?.AuthorizedContactsJson),
            JobSites = ParseJson(account?.JobSitesJson),
            Invoiced = financials?.Invoiced ?? 0, Paid = financials?.Paid ?? 0,
            Outstanding = financials?.Outstanding ?? 0,
            AvailableCredit = Math.Max(0, (account?.CreditLimit ?? 0) - (financials?.Outstanding ?? 0)),
        });
    }

    [HttpGet("documents")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> Documents(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null) return Unauthorized();
        var bookings = await db.Bookings.AsNoTracking().Where(x => x.CustomerId == user.CustomerId)
            .Select(x => new { x.Id, Reference = x.BookingNumber }).ToListAsync(token);
        var bookingIds = bookings.Select(x => x.Id).ToList();
        var references = bookings.ToDictionary(x => x.Id, x => x.Reference);
        var registered = await db.DocumentRecords.AsNoTracking().Where(x =>
                x.EntityType == nameof(Customer) && x.EntityId == user.CustomerId ||
                x.EntityType == nameof(Booking) && bookingIds.Contains(x.EntityId))
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.EntityType, x.EntityId,
                x.DocumentNumber, x.Type, x.FileName, x.ExpiresOn, x.CreatedAt }).ToListAsync(token);
        var agreements = await db.RentalAgreements.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId))
            .OrderByDescending(x => x.CustomerSignedAt).Select(x => new { x.Id, x.BookingId,
                Number = x.AgreementNumber, x.Status, CreatedAt = x.CustomerSignedAt }).ToListAsync(token);
        var invoices = await db.RentalInvoices.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId))
            .OrderByDescending(x => x.IssuedAt).Select(x => new { x.Id, x.BookingId,
                Number = x.InvoiceNumber, x.Status, x.Total, x.BalanceDue, CreatedAt = x.IssuedAt }).ToListAsync(token);
        return Ok(new
        {
            Registered = registered.Select(x => new { x.Id, x.DocumentNumber, x.Type, x.FileName,
                x.ExpiresOn, x.CreatedAt, BookingReference = x.EntityType == nameof(Booking) ? references.GetValueOrDefault(x.EntityId) : null }),
            Agreements = agreements.Select(x => new { x.Id, x.Number, x.Status, x.CreatedAt,
                BookingReference = references.GetValueOrDefault(x.BookingId), x.BookingId }),
            Invoices = invoices.Select(x => new { x.Id, x.Number, x.Status, x.Total, x.BalanceDue, x.CreatedAt,
                BookingReference = references.GetValueOrDefault(x.BookingId), x.BookingId }),
        });
    }

    [HttpPost("documents/upload")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    [RequestSizeLimit(5_500_000)]
    public async Task<ActionResult> UploadDocument([FromForm] CustomerDocumentUploadRequest request, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null) return Unauthorized();
        if (!string.Equals(request.Type, "DriverLicence", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only a driver licence can be uploaded from the customer portal." });
        if (request.File.Length is <= 0 or > 5_242_880)
            return BadRequest(new { message = "Choose a PDF, JPEG or PNG file no larger than 5 MB." });
        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".jpg" or ".jpeg" or ".png"))
            return BadRequest(new { message = "Only PDF, JPEG and PNG files are accepted." });
        await using var content = new MemoryStream();
        await request.File.CopyToAsync(content, token);
        var bytes = content.ToArray();
        if (!HasValidSignature(extension, bytes))
            return BadRequest(new { message = "The file content does not match its extension." });

        Guid entityId = user.CustomerId.Value; var entityType = nameof(Customer); Guid? branchId = null;
        if (request.BookingId.HasValue)
        {
            var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.BookingId && x.CustomerId == user.CustomerId, token);
            if (booking is null) return NotFound();
            entityId = booking.Id; entityType = nameof(Booking); branchId = booking.BranchId;
        }
        var root = Path.Combine(environment.ContentRootPath, "App_Data", "customer-documents", user.CustomerId.Value.ToString("N"));
        Directory.CreateDirectory(root);
        var storageName = $"{Guid.NewGuid():N}{extension}";
        await System.IO.File.WriteAllBytesAsync(Path.Combine(root, storageName), bytes, token);
        if (!request.BookingId.HasValue)
        {
            var previous = await db.DocumentRecords.Where(document => document.EntityType == nameof(Customer) &&
                document.EntityId == user.CustomerId && document.Type == "DriverLicence").ToListAsync(token);
            foreach (var old in previous)
            {
                var oldPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "customer-documents", old.StoragePath));
                if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
            }
            db.DocumentRecords.RemoveRange(previous);
        }
        var record = new DocumentRecord { DocumentNumber = Number("DOC"), EntityType = entityType, EntityId = entityId,
            BranchId = branchId, Type = request.Type, FileName = Path.GetFileName(request.File.FileName),
            StoragePath = Path.Combine(user.CustomerId.Value.ToString("N"), storageName),
            ExpiresOn = request.ExpiresOn, ContentHash = Convert.ToHexString(SHA256.HashData(bytes)) };
        db.DocumentRecords.Add(record); await db.SaveChangesAsync(token);
        return Ok(new { record.Id, record.DocumentNumber, record.Type, record.FileName, record.ExpiresOn });
    }

    [HttpGet("documents/{documentId:guid}/download")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> DownloadDocument(Guid documentId, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User); if (user?.CustomerId is null) return Unauthorized();
        var bookingIds = db.Bookings.AsNoTracking().Where(x => x.CustomerId == user.CustomerId).Select(x => x.Id);
        var record = await db.DocumentRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == documentId &&
            (x.EntityType == nameof(Customer) && x.EntityId == user.CustomerId || x.EntityType == nameof(Booking) && bookingIds.Contains(x.EntityId)), token);
        if (record is null) return NotFound();
        var storageRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "customer-documents"));
        var path = Path.GetFullPath(Path.Combine(storageRoot, record.StoragePath));
        if (!path.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !System.IO.File.Exists(path)) return NotFound();
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch { ".pdf" => "application/pdf", ".png" => "image/png", _ => "image/jpeg" };
        return PhysicalFile(path, contentType, record.FileName);
    }

    [HttpPut("profile")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> UpdateProfile(CustomerProfileRequest request, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null) return Unauthorized();
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == user.CustomerId, token);
        if (customer is null) return NotFound();
        user.FullName = request.FullName.Trim();
        customer.Phone = request.Phone.Trim();
        customer.Address = Clean(request.Address);
        customer.IdentificationNumber = Clean(request.IdentificationNumber);
        customer.HirePreferences = request.HirePreferences.Distinct().ToList();
        await userManager.UpdateAsync(user);
        await db.SaveChangesAsync(token);
        return Ok(new { user.FullName, customer.Phone, customer.Address, customer.IdentificationNumber, customer.HirePreferences });
    }

    [HttpPut("bookings/{bookingId:guid}/dates")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> ChangeDates(Guid bookingId, CustomerBookingDatesRequest request, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null) return Unauthorized();
        var booking = await db.Bookings.Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.CustomerId == user.CustomerId, token);
        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.Draft)
            return Conflict(new { message = "Dates can only be changed while the request is awaiting review." });
        if (request.EndAt <= request.StartAt || request.StartAt < DateTimeOffset.UtcNow.Date)
            return BadRequest(new { message = "Choose a valid future pickup and return date." });
        var assetIds = booking.Items.Select(x => x.AssetId).ToList();
        var conflict = await db.BookingItems.AsNoTracking().AnyAsync(x => x.BookingId != booking.Id &&
            assetIds.Contains(x.AssetId) && x.StartAt < request.EndAt && x.EndAt > request.StartAt &&
            (x.Booking!.Status == BookingStatus.Confirmed || x.Booking.Status == BookingStatus.ConvertedToRental), token);
        if (conflict) return Conflict(new { message = "The rental is unavailable for those dates. Contact the branch for an alternative." });
        foreach (var item in booking.Items) { item.StartAt = request.StartAt; item.EndAt = request.EndAt; }
        booking.Notes = Append(booking.Notes, $"Customer changed requested dates online to {request.StartAt:u} – {request.EndAt:u}.");
        await db.SaveChangesAsync(token);
        return Ok(new { booking.Id, StartAt = request.StartAt, EndAt = request.EndAt });
    }

    [HttpPost("bookings/{bookingId:guid}/resend-confirmation")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult> ResendConfirmation(Guid bookingId, CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);
        if (user?.CustomerId is null || string.IsNullOrWhiteSpace(user.Email)) return Unauthorized();
        var booking = await db.Bookings.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Asset).Include(x => x.Branch)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.CustomerId == user.CustomerId, token);
        if (booking is null) return NotFound();
        var item = booking.Items.OrderBy(x => x.StartAt).FirstOrDefault();
        var html = EmailTemplate.Branded($"Booking {booking.BookingNumber}", $"<p>Hello {System.Net.WebUtility.HtmlEncode(user.FullName)},</p><p>Your rental request is currently <strong>{booking.Status}</strong>.</p><p><strong>{System.Net.WebUtility.HtmlEncode(item?.Asset?.Name ?? "Rental request")}</strong><br>{item?.StartAt:dd MMM yyyy} to {item?.EndAt:dd MMM yyyy}<br>{System.Net.WebUtility.HtmlEncode(booking.Branch?.Name)}</p><p>Sign in to your customer account for the latest progress and pickup requirements.</p>");
        emailQueue.Queue(db, user.Email, $"CREMS booking {booking.BookingNumber}", html,
            $"Booking {booking.BookingNumber}: {booking.Status}. Sign in to CREMS for details.", "BookingConfirmation");
        await db.SaveChangesAsync(token);
        return Accepted(new { message = "Confirmation email queued." });
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
        var subject = $"Extension request — {booking.BookingNumber}";
        if (await db.CustomerCases.AnyAsync(x => x.CustomerId == user.CustomerId && x.Subject == subject && x.Status != CaseStatus.Resolved && x.Status != CaseStatus.Closed, token))
            return Conflict(new { message = "An extension request for this booking is already awaiting branch review." });
        var extensionCase = new CustomerCase { CaseNumber = Number("CASE"), CustomerId = user.CustomerId.Value, BranchId = booking.BranchId, Type = CaseType.Enquiry, Priority = booking.Status == BookingStatus.ConvertedToRental ? CasePriority.High : CasePriority.Normal, Subject = subject, Description = $"Current return: {currentEnd:u}. Requested new return: {request.RequestedEndAt:u}. Reason: {Clean(request.Reason) ?? "Not supplied"}", DueAt = DateTimeOffset.UtcNow.AddHours(4) };
        db.CustomerCases.Add(extensionCase);
        await db.SaveChangesAsync(token); return Accepted(new { status = "Extension requested", extensionCase.CaseNumber, currentEnd, request.RequestedEndAt });
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

    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return null; }
    }

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<IReadOnlyList<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private async Task<string> NextCustomerNumber(CancellationToken token)
    {
        var numbers = await db.Customers.AsNoTracking().Select(customer => customer.CustomerNumber).ToListAsync(token);
        var next = numbers.Select(number => number.StartsWith("CUS-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(number[4..], out var value) ? value : 0).DefaultIfEmpty().Max() + 1;
        string candidate;
        do candidate = $"CUS-{next++:D6}";
        while (numbers.Contains(candidate, StringComparer.OrdinalIgnoreCase));
        return candidate;
    }
    private static bool HasValidSignature(string extension, byte[] bytes) => extension switch
    {
        ".pdf" => bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46,
        ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        ".png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        _ => false,
    };
}

public sealed class CustomerDocumentUploadRequest
{
    [Required] public required IFormFile File { get; init; }
    [Required, MaxLength(50)] public required string Type { get; init; }
    public Guid? BookingId { get; init; }
    public DateOnly? ExpiresOn { get; init; }
}

public sealed record VerifyEmailRequest([Required, RegularExpression("^[0-9]{6}$")] string Code);
public sealed record CustomerBookingChangeRequest([MaxLength(1000)] string? Reason);
public sealed record CustomerExtensionRequest(DateTimeOffset RequestedEndAt, [MaxLength(1000)] string? Reason);
public sealed record CustomerIncidentRequest(IncidentType Type, DateTimeOffset OccurredAt, [Required, MaxLength(2000)] string Description, [MaxLength(500)] string? Location, [MaxLength(100)] string? PoliceReference);
public sealed record CustomerQuoteDecisionRequest(bool Accepted, [MaxLength(1000)] string? Note);
public sealed record CustomerProfileRequest([Required, MaxLength(150)] string FullName,
    [Required, MaxLength(50)] string Phone, [MaxLength(500)] string? Address,
    [MaxLength(100)] string? IdentificationNumber,
    [Required, MaxLength(3)] CustomerHirePreference[] HirePreferences);
public sealed record CustomerBookingDatesRequest(DateTimeOffset StartAt, DateTimeOffset EndAt);
public sealed record ActivateCustomerAccountRequest([Required, EmailAddress] string Email,
    [Required, RegularExpression("^[0-9]{6}$")] string Code, [Required, MinLength(10)] string Password);

public sealed record RegisterCustomerRequest(
    [Required, MaxLength(150)] string FullName,
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(50)] string Phone,
    [MaxLength(500)] string? Address,
    [MaxLength(100)] string? IdentificationNumber,
    [Required, MaxLength(3)] CustomerHirePreference[] HirePreferences,
    [Required, MinLength(10)] string Password);
