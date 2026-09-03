using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Text.Json;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/public")]
public sealed class PublicRentalsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailQueue emailQueue, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("assets/{assetId:guid}/photos/{fileName}")]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult> GetAssetPhoto(Guid assetId, string fileName, CancellationToken token)
    {
        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal)) return BadRequest();
        var expectedUrl = $"/api/public/assets/{assetId}/photos/{safeName}";
        var json = await db.Assets.AsNoTracking().Where(x => x.Id == assetId && x.IsActive).Select(x => x.PhotoUrlsJson).FirstOrDefaultAsync(token);
        if (json is null || !ParseJsonArray(json).Contains(expectedUrl, StringComparer.Ordinal)) return NotFound();
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "asset-images", assetId.ToString("N")));
        var path = Path.GetFullPath(Path.Combine(root, safeName));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !System.IO.File.Exists(path)) return NotFound();
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    [HttpGet("divisions")]
    [AllowAnonymous]
    public async Task<ActionResult> GetDivisions(CancellationToken cancellationToken) => Ok(
        await db.Divisions.AsNoTracking().Where(x => x.IsActive && x.IsPublic)
            .OrderBy(x => x.Name).Select(x => new
            {
                x.Id, x.Code, x.Name, x.Description, x.Capabilities,
                Services = x.ServiceOfferings.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new
                { s.Id, s.Code, s.Name, s.Description, s.Type, s.PersonnelRequirement, s.IsBookableOnline, s.RequiresQuote })
            }).ToListAsync(cancellationToken));

    [HttpGet("branches")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicBranchResponse>>> GetBranches(
        CancellationToken cancellationToken)
    {
        var branches = await db.Branches.AsNoTracking()
            .Where(branch => branch.IsActive)
            .OrderBy(branch => branch.Name)
            .Select(branch => new PublicBranchResponse(
                branch.Id, branch.Name, branch.Address, branch.Phone))
            .ToListAsync(cancellationToken);
        return Ok(branches);
    }

    [HttpGet("assets")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicAssetResponse>>> GetAssets(
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? divisionId,
        [FromQuery] AssetType? type,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        if (startDate.HasValue != endDate.HasValue ||
            startDate.HasValue && endDate <= startDate)
        {
            ModelState.AddModelError(nameof(endDate), "Choose a return date after the pickup date.");
            return ValidationProblem(ModelState);
        }
        if (startDate.HasValue && startDate < DateOnly.FromDateTime(DateTime.UtcNow))
        { ModelState.AddModelError(nameof(startDate), "Pickup cannot be in the past."); return ValidationProblem(ModelState); }

        var query = db.Assets.AsNoTracking()
            .Where(asset => asset.IsActive && asset.Branch!.IsActive &&
                asset.Status != AssetStatus.Maintenance &&
                asset.Status != AssetStatus.OutOfService &&
                asset.Status != AssetStatus.Retired);
        if (branchId.HasValue) query = query.Where(asset => asset.BranchId == branchId);
        if (divisionId.HasValue) query = query.Where(asset => asset.DivisionId == divisionId);
        if (type.HasValue) query = query.Where(asset => asset.Type == type);

        HashSet<Guid> unavailableAssetIds = [];
        if (startDate.HasValue && endDate.HasValue)
        {
            var start = new DateTimeOffset(startDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset(endDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            unavailableAssetIds = (await db.BookingItems.AsNoTracking()
                .Where(item => item.StartAt < end && item.EndAt > start &&
                    (item.Booking!.Status == BookingStatus.Confirmed ||
                     item.Booking.Status == BookingStatus.ConvertedToRental ||
                     item.Booking.Status == BookingStatus.Draft && item.Booking.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-30)))
                .Select(item => item.AssetId)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();
        }

        var assets = await query.OrderBy(asset => asset.Type).ThenBy(asset => asset.Name)
            .Select(asset => new
            {
                asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status,
                asset.BranchId, BranchName = asset.Branch!.Name, asset.DailyRate,
                asset.RegistrationNumber, asset.SerialNumber, asset.DivisionId,
                DivisionName = asset.Division != null ? asset.Division.Name : null,
                asset.Category, asset.PersonnelRequirement,
                ServiceName = asset.ServiceOffering != null ? asset.ServiceOffering.Name : null,
                RequiresQuote = asset.ServiceOffering != null && asset.ServiceOffering.RequiresQuote,
                RequiresDelivery = asset.ServiceOffering != null && asset.ServiceOffering.RequiresDelivery,
                asset.PhotoUrlsJson,
                Attributes = asset.AttributeValues
                    .Where(value => value.AttributeDefinition != null &&
                        value.AttributeDefinition.IsCustomerVisible && value.AttributeDefinition.IsSearchable)
                    .OrderBy(value => value.AttributeDefinition!.DisplayOrder)
                    .Select(value => new PublicAssetAttributeResponse(
                        value.AttributeDefinition!.Code, value.AttributeDefinition.Name,
                        value.Value ?? string.Empty, value.AttributeDefinition.Unit))
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return Ok(assets.Select(asset => new PublicAssetResponse(
            asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status,
            asset.BranchId, asset.BranchName, asset.DailyRate,
            asset.RegistrationNumber, asset.SerialNumber,
            asset.DivisionId, asset.DivisionName, asset.Category, asset.PersonnelRequirement,
            asset.ServiceName, asset.RequiresQuote, asset.RequiresDelivery, asset.PhotoUrlsJson,
            asset.Attributes,
            startDate.HasValue
                ? !unavailableAssetIds.Contains(asset.Id)
                : asset.Status == AssetStatus.Available)).ToList());
    }

    [HttpGet("assets/{assetId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult> GetAssetDetails(
        Guid assetId,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        if (startDate.HasValue != endDate.HasValue || startDate.HasValue && endDate <= startDate)
        {
            ModelState.AddModelError(nameof(endDate), "Choose a return date after the pickup date.");
            return ValidationProblem(ModelState);
        }
        if (startDate.HasValue && startDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError(nameof(startDate), "Pickup cannot be in the past.");
            return ValidationProblem(ModelState);
        }

        var asset = await db.Assets.AsNoTracking()
            .Include(x => x.Branch).Include(x => x.Division).Include(x => x.ServiceOffering)
            .Include(x => x.AssetCategory).Include(x => x.AttributeValues).ThenInclude(x => x.AttributeDefinition)
            .FirstOrDefaultAsync(x => x.Id == assetId && x.IsActive && x.Branch!.IsActive &&
                x.Status != AssetStatus.Maintenance && x.Status != AssetStatus.OutOfService &&
                x.Status != AssetStatus.Retired, cancellationToken);
        if (asset is null) return NotFound();

        var isAvailable = asset.Status == AssetStatus.Available;
        if (startDate.HasValue && endDate.HasValue)
        {
            var start = new DateTimeOffset(startDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset(endDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            isAvailable = !await db.BookingItems.AsNoTracking().AnyAsync(item => item.AssetId == asset.Id &&
                item.StartAt < end && item.EndAt > start &&
                (item.Booking!.Status == BookingStatus.Confirmed ||
                 item.Booking.Status == BookingStatus.ConvertedToRental ||
                 item.Booking.Status == BookingStatus.Draft && item.Booking.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-30)),
                cancellationToken);
        }

        var customerAttributes = asset.AttributeValues
            .Where(x => x.AttributeDefinition?.IsCustomerVisible == true && !string.IsNullOrWhiteSpace(x.Value))
            .OrderBy(x => x.AttributeDefinition!.DisplayOrder)
            .Select(x => new { x.AttributeDefinition!.Name, x.Value, x.AttributeDefinition.Unit })
            .ToList();
        var visibleCharges = asset.ServiceOffering is null ? [] : await db.ChargeDefinitions.AsNoTracking()
            .Where(x => x.IsActive && x.IsCustomerVisible && x.Category != ChargeCategory.BaseHire &&
                x.DivisionId == asset.DivisionId &&
                (!x.ServiceOfferingId.HasValue || x.ServiceOfferingId == asset.ServiceOfferingId))
            .OrderBy(x => x.Category).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Category, x.Unit, x.DefaultSellingRate, x.IsRequired })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            asset.Id, asset.Name, asset.Type, asset.Category, asset.DailyRate, asset.PersonnelRequirement,
            asset.Manufacturer, asset.Model, asset.ModelYear, asset.PhotoUrlsJson,
            Division = asset.Division is null ? null : new { asset.Division.Id, asset.Division.Code, asset.Division.Name },
            Branch = new { asset.BranchId, asset.Branch!.Name, asset.Branch.Address, asset.Branch.Phone,
                asset.Branch.Email, asset.Branch.PickupInstructions, asset.Branch.DeliveryCoverage },
            Service = asset.ServiceOffering is null ? null : new
            {
                asset.ServiceOffering.Id, asset.ServiceOffering.Code, asset.ServiceOffering.Name,
                asset.ServiceOffering.Description, asset.ServiceOffering.Type,
                asset.ServiceOffering.PersonnelRequirement, asset.ServiceOffering.IsBookableOnline,
                asset.ServiceOffering.RequiresQuote, asset.ServiceOffering.DefaultHireUnit,
                asset.ServiceOffering.RequiresDelivery, asset.ServiceOffering.DefaultDepositAmount,
                RequiredDocuments = ParseJsonArray(asset.ServiceOffering.RequiredDocumentsJson),
            },
            Specifications = customerAttributes, Charges = visibleCharges, IsAvailable = isAvailable,
            AvailabilityChecked = startDate.HasValue,
        });
    }

    [HttpPost("booking-requests")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult<PublicBookingResponse>> RequestBooking(
        PublicBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.EndDate <= request.StartDate)
        {
            ModelState.AddModelError(nameof(request.EndDate), "Choose a return date after the pickup date.");
            return ValidationProblem(ModelState);
        }
        if (request.StartDate < DateOnly.FromDateTime(DateTime.UtcNow))
        { ModelState.AddModelError(nameof(request.StartDate), "Pickup cannot be in the past."); return ValidationProblem(ModelState); }
        if (request.EndDate.DayNumber - request.StartDate.DayNumber > 366)
        { ModelState.AddModelError(nameof(request.EndDate), "Online requests cannot exceed 12 months. Contact the branch for long-term hire."); return ValidationProblem(ModelState); }
        if (request.CustomerType == CustomerType.Business && string.IsNullOrWhiteSpace(request.CompanyName))
        {
            ModelState.AddModelError(nameof(request.CompanyName), "Enter the registered business name.");
            return ValidationProblem(ModelState);
        }

        var asset = await db.Assets.Include(item => item.Branch).Include(item => item.Division).Include(item => item.ServiceOffering)
            .FirstOrDefaultAsync(item => item.Id == request.AssetId && item.IsActive, cancellationToken);
        if (asset is null || asset.Branch is null || !asset.Branch.IsActive ||
            asset.Status is AssetStatus.Maintenance or AssetStatus.OutOfService or AssetStatus.Retired)
            return NotFound("The selected rental item is no longer available.");

        var start = new DateTimeOffset(request.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(request.EndDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var overlaps = await db.BookingItems.AnyAsync(item =>
            item.AssetId == asset.Id && item.StartAt < end && item.EndAt > start &&
            (item.Booking!.Status == BookingStatus.Confirmed ||
             item.Booking.Status == BookingStatus.ConvertedToRental ||
             item.Booking.Status == BookingStatus.Draft && item.Booking.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-30)), cancellationToken);
        if (overlaps)
        {
            ModelState.AddModelError(nameof(request.AssetId), "This item is no longer available for the selected dates.");
            return ValidationProblem(ModelState);
        }

        var signedInUser = await userManager.GetUserAsync(User);
        if (signedInUser?.CustomerId is null) return Unauthorized();
        var customer = await db.Customers.FirstOrDefaultAsync(
            item => item.Id == signedInUser.CustomerId && item.IsActive, cancellationToken);
        if (customer is not { } activeCustomer) return Unauthorized();
        if (activeCustomer.IsBlocked)
            return ValidationProblem("We cannot accept this request online. Please contact the rental team.");
        // Keep the registered identity authoritative while allowing current contact details.
        activeCustomer.Phone = request.Phone.Trim();
        activeCustomer.Address = Normalize(request.Address) ?? activeCustomer.Address;
        activeCustomer.IdentificationNumber = Normalize(request.IdentificationNumber) ?? activeCustomer.IdentificationNumber;

        var hireDays = Math.Max(1, request.EndDate.DayNumber - request.StartDate.DayNumber);
        var availableCharges = await db.ChargeDefinitions.AsNoTracking().Where(x => x.IsActive && x.IsCustomerVisible && x.Category != ChargeCategory.BaseHire &&
            x.DivisionId == asset.DivisionId && (!x.ServiceOfferingId.HasValue || x.ServiceOfferingId == asset.ServiceOfferingId))
            .ToListAsync(cancellationToken);
        var assetDescription = $"{asset.Category} {asset.Name}";
        var heavyEquipment = asset.Type == AssetType.Equipment && new[] { "heavy", "excavator", "crane", "backhoe", "loader" }
            .Any(value => assetDescription.Contains(value, StringComparison.OrdinalIgnoreCase));
        var personnelRequested = asset.PersonnelRequirement == PersonnelRequirement.Required || heavyEquipment || request.PersonnelRequested;
        if (personnelRequested && !availableCharges.Any(x => x.Category is ChargeCategory.Operator or ChargeCategory.Driver))
            return Conflict(new { message = "Professional personnel is required or selected, but no operator or driver rate is configured for this service. Please contact the branch." });
        var selectedExtras = (request.Extras ?? []).Where(x => x.Quantity > 0 && x.Quantity <= 1000)
            .GroupBy(x => x.ChargeDefinitionId).ToDictionary(x => x.Key, x => x.First().Quantity);
        var charges = availableCharges.Where(x => x.IsRequired || selectedExtras.ContainsKey(x.Id) ||
                request.Fulfilment == "Delivery" && x.Category == ChargeCategory.Transport ||
                personnelRequested && x.Category is ChargeCategory.Operator or ChargeCategory.Driver)
            .Select(x => new
            {
                Definition = x,
                Quantity = selectedExtras.GetValueOrDefault(x.Id, x.Unit switch
                {
                    ChargeUnit.Day => hireDays,
                    ChargeUnit.Hour => Math.Max(1, request.PersonnelHours ?? hireDays * 8),
                    _ => 1,
                })
            }).ToList();
        var baseSubtotal = hireDays * asset.DailyRate;
        var chargeSubtotal = charges.Sum(x => x.Quantity * x.Definition.DefaultSellingRate);
        var taxRate = asset.Division?.DefaultTaxRate ?? 15m;
        var tax = decimal.Round((baseSubtotal + chargeSubtotal) * taxRate / 100m, 2);
        var total = baseSubtotal + chargeSubtotal + tax;
        var requiresQuote = activeCustomer.Type == CustomerType.Business || asset.Type == AssetType.Equipment ||
            asset.ServiceOffering?.RequiresQuote == true || asset.PersonnelRequirement != PersonnelRequirement.None;

        if (requiresQuote)
        {
            var lineItems = new List<object> { new { Description = $"{asset.Name} hire", Quantity = hireDays, Rate = asset.DailyRate, Unit = "Day", Amount = baseSubtotal } };
            lineItems.AddRange(charges.Select(x => (object)new { Description = x.Definition.Name, x.Quantity,
                Rate = x.Definition.DefaultSellingRate, Unit = x.Definition.Unit.ToString(), Amount = x.Quantity * x.Definition.DefaultSellingRate }));
            var quote = new SalesQuote
            {
                QuoteNumber = $"QUO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                CustomerId = activeCustomer.Id, BranchId = asset.BranchId, DivisionId = asset.DivisionId,
                Status = QuoteStatus.Draft, ValidUntil = DateTimeOffset.UtcNow.AddDays(14),
                JobSite = Normalize(request.DeliveryAddress), PurchaseOrderNumber = Normalize(request.PurchaseOrderNumber),
                Subtotal = baseSubtotal + chargeSubtotal, Tax = tax, Total = total,
                LineItemsJson = JsonSerializer.Serialize(lineItems),
            };
            db.SalesQuotes.Add(quote);
            db.CustomerCases.Add(new CustomerCase { CaseNumber = $"CASE-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                CustomerId = activeCustomer.Id, BranchId = asset.BranchId, Type = CaseType.Enquiry,
                Priority = CasePriority.Normal, Subject = $"Online quotation request — {quote.QuoteNumber}",
                Description = BuildRequestNotes(request, personnelRequested), DueAt = DateTimeOffset.UtcNow.AddHours(8) });
            await db.SaveChangesAsync(cancellationToken);
            QueueCustomerConfirmation(signedInUser, quote.QuoteNumber, asset.Name, request.StartDate, request.EndDate,
                "Quotation request received", "Our team will review availability, transport, personnel and final charges. You can follow the quotation in your customer account.");
            await db.SaveChangesAsync(cancellationToken);
            return Ok(new PublicBookingResponse(quote.QuoteNumber, asset.Name, asset.Branch.Name,
                request.StartDate, request.EndDate, "Quotation", "Quotation request received"));
        }

        var booking = new Booking
        {
            BookingNumber = $"REQ-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
            Customer = activeCustomer,
            BranchId = asset.BranchId,
            Status = BookingStatus.Draft,
            Notes = BuildRequestNotes(request, personnelRequested),
            TaxRate = taxRate,
            DepositRequired = asset.ServiceOffering?.DefaultDepositAmount ?? 0,
            Items =
            [
                new BookingItem
                {
                    AssetId = asset.Id,
                    StartAt = start,
                    EndAt = end,
                    DailyRate = asset.DailyRate,
                }
            ],
        };
        foreach (var charge in charges)
            booking.Charges.Add(new BookingCharge { AssetId = asset.Id, ChargeDefinitionId = charge.Definition.Id,
                Description = charge.Definition.Name, Category = charge.Definition.Category, Unit = charge.Definition.Unit,
                Quantity = charge.Quantity, UnitRate = charge.Definition.DefaultSellingRate,
                UnitCost = charge.Definition.DefaultCostRate, IsTaxable = charge.Definition.IsTaxable });
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);

        if (request.Fulfilment == "Delivery")
            db.DispatchJobs.Add(new DispatchJob { DispatchNumber = $"DSP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                BookingId = booking.Id, BranchId = booking.BranchId, Type = DispatchType.Delivery,
                ScheduledAt = start, Address = Normalize(request.DeliveryAddress), Status = DispatchStatus.Scheduled,
                DeliveryCharge = charges.Where(x => x.Definition.Category == ChargeCategory.Transport).Sum(x => x.Quantity * x.Definition.DefaultSellingRate) });
        QueueCustomerConfirmation(signedInUser, booking.BookingNumber, asset.Name, request.StartDate, request.EndDate,
            "Booking request received", "The branch will verify your details and confirm the booking. You can follow pickup requirements in your customer account.");
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new PublicBookingResponse(
            booking.BookingNumber, asset.Name, asset.Branch.Name,
            request.StartDate, request.EndDate, "Booking", "Booking request received"));
    }

    private void QueueCustomerConfirmation(ApplicationUser user, string reference, string assetName,
        DateOnly startDate, DateOnly endDate, string heading, string explanation)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return;
        var html = EmailTemplate.Branded(heading, $"<p>Hello {System.Net.WebUtility.HtmlEncode(user.FullName)},</p><p>We received your request for <strong>{System.Net.WebUtility.HtmlEncode(assetName)}</strong>.</p><p><strong>Reference:</strong> {reference}<br><strong>Dates:</strong> {startDate:dd MMM yyyy} to {endDate:dd MMM yyyy}</p><p>{System.Net.WebUtility.HtmlEncode(explanation)}</p>");
        emailQueue.Queue(db, user.Email, $"{heading} — {reference}", html,
            $"{heading}. Reference {reference}. {assetName}, {startDate:dd MMM yyyy} to {endDate:dd MMM yyyy}.", "BookingConfirmation");
    }

    private static string BuildRequestNotes(PublicBookingRequest request, bool personnelRequested)
    {
        var details = new List<string>
        {
            "Submitted through the public rental website.",
            $"Contact person: {request.FullName.Trim()}",
        };
        if (!string.IsNullOrWhiteSpace(request.Purpose))
            details.Add($"Rental purpose: {request.Purpose.Trim()}");
        details.Add($"Fulfilment requested: {request.Fulfilment}.");
        if (!string.IsNullOrWhiteSpace(request.DeliveryAddress))
            details.Add($"Delivery/worksite address: {request.DeliveryAddress.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.SiteContact))
            details.Add($"Site contact/access: {request.SiteContact.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.PurchaseOrderNumber))
            details.Add($"Purchase order: {request.PurchaseOrderNumber.Trim()}");
        if (personnelRequested)
            details.Add($"Customer requested trained personnel{(request.PersonnelHours.HasValue ? $" for approximately {request.PersonnelHours} hours" : "")}.");
        if (!string.IsNullOrWhiteSpace(request.DriverName))
            details.Add($"Nominated driver: {request.DriverName.Trim()}.");
        if (!string.IsNullOrWhiteSpace(request.DriverLicence))
            details.Add($"Driver licence supplied for verification: {request.DriverLicence.Trim()}.");
        if (!string.IsNullOrWhiteSpace(request.Message))
            details.Add($"Customer message: {request.Message.Trim()}");
        return string.Join("\n", details);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> ParseJsonArray(string json)
    {
        try { return JsonSerializer.Deserialize<IReadOnlyList<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    [HttpGet("booking-status/{reference}")]
    [Authorize(Policy = SystemPolicies.CustomerPortal)]
    public async Task<ActionResult<PublicBookingStatusResponse>> GetBookingStatus(
        string reference,
        CancellationToken cancellationToken)
    {
        var signedInUser = await userManager.GetUserAsync(User);
        if (signedInUser?.CustomerId is null) return Unauthorized();
        var normalizedReference = reference.Trim().ToUpperInvariant();
        var booking = await db.Bookings.AsNoTracking()
            .Where(item => item.BookingNumber == normalizedReference && item.CustomerId == signedInUser.CustomerId)
            .Select(item => new
            {
                item.BookingNumber,
                item.Status,
                item.CreatedAt,
                BranchName = item.Branch!.Name,
                RentalItem = item.Items.OrderBy(bookingItem => bookingItem.StartAt)
                    .Select(bookingItem => new
                    {
                        bookingItem.Asset!.Name,
                        bookingItem.StartAt,
                        bookingItem.EndAt,
                    }).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (booking is null) return NotFound();

        return Ok(new PublicBookingStatusResponse(
            booking.BookingNumber,
            GetPublicStatus(booking.Status),
            GetPublicMessage(booking.Status),
            booking.RentalItem?.Name,
            booking.BranchName,
            booking.RentalItem?.StartAt,
            booking.RentalItem?.EndAt,
            booking.CreatedAt));
    }

    private static string GetPublicStatus(BookingStatus status) => status switch
    {
        BookingStatus.Draft => "Awaiting review",
        BookingStatus.Confirmed => "Confirmed",
        BookingStatus.Cancelled => "Not proceeding",
        BookingStatus.ConvertedToRental => "Rental active",
        BookingStatus.Completed => "Completed",
        BookingStatus.Expired => "Expired",
        _ => "Under review",
    };

    private static string GetPublicMessage(BookingStatus status) => status switch
    {
        BookingStatus.Draft => "Your request has been received and is waiting for review by our rental team.",
        BookingStatus.Confirmed => "Your booking has been confirmed. Our team will contact you with the pickup requirements.",
        BookingStatus.Cancelled => "This request is not proceeding. Please contact the rental branch if you need assistance.",
        BookingStatus.ConvertedToRental => "Your booking has been converted to an active rental.",
        BookingStatus.Completed => "This rental has been completed. Thank you for choosing Carpenters Rentals.",
        BookingStatus.Expired => "This booking request has expired. Please submit a new request if you still need the rental.",
        _ => "Please contact the rental branch for more information.",
    };
}

public sealed record PublicBranchResponse(Guid Id, string Name, string? Address, string? Phone);
public sealed record PublicAssetResponse(
    Guid Id, string AssetNumber, string Name, AssetType Type, AssetStatus Status,
    Guid BranchId, string BranchName, decimal DailyRate,
    string? RegistrationNumber, string? SerialNumber, Guid? DivisionId,
    string? DivisionName, string? Category, PersonnelRequirement PersonnelRequirement,
    string? ServiceName, bool RequiresQuote, bool RequiresDelivery, string PhotoUrlsJson,
    IReadOnlyList<PublicAssetAttributeResponse> Attributes, bool IsAvailable);
public sealed record PublicAssetAttributeResponse(string Code, string Name, string Value, string? Unit);
public sealed record PublicBookingRequest(
    Guid AssetId,
    DateOnly StartDate,
    DateOnly EndDate,
    [Required, MaxLength(150)] string FullName,
    CustomerType CustomerType,
    [MaxLength(150)] string? CompanyName,
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(50)] string Phone,
    [MaxLength(500)] string? Address,
    [MaxLength(100)] string? IdentificationNumber,
    [MaxLength(250)] string? Purpose,
    [MaxLength(1000)] string? Message,
    [MaxLength(20)] string? Fulfilment,
    [MaxLength(500)] string? DeliveryAddress,
    [MaxLength(500)] string? SiteContact,
    [MaxLength(100)] string? PurchaseOrderNumber,
    bool PersonnelRequested,
    decimal? PersonnelHours,
    [MaxLength(150)] string? DriverName,
    [MaxLength(100)] string? DriverLicence,
    IReadOnlyList<PublicBookingExtraRequest>? Extras);
public sealed record PublicBookingExtraRequest(Guid ChargeDefinitionId, decimal Quantity);
public sealed record PublicBookingResponse(
    string Reference, string AssetName, string BranchName,
    DateOnly StartDate, DateOnly EndDate, string RequestType, string Status);
public sealed record PublicBookingStatusResponse(
    string Reference, string Status, string Message, string? AssetName,
    string BranchName, DateTimeOffset? StartAt, DateTimeOffset? EndAt,
    DateTimeOffset SubmittedAt);
