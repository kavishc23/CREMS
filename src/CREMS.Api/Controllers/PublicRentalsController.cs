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
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/public")]
public sealed class PublicRentalsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailQueue emailQueue, IWebHostEnvironment environment, IMemoryCache? memoryCache = null) : ControllerBase
{
    [HttpGet("catalogue-bootstrap")]
    [AllowAnonymous]
    public async Task<ActionResult> GetCatalogueBootstrap(CancellationToken token)
    {
        const string cacheKey = "public-catalogue-bootstrap-v1";
        if (memoryCache?.TryGetValue(cacheKey, out object? cached) == true && cached is not null)
        {
            Response.Headers.CacheControl = "public, max-age=30, stale-while-revalidate=60";
            return Ok(cached);
        }

        var branches = await db.Branches.AsNoTracking()
            .Where(branch => branch.IsActive && branch.IsPublic)
            .OrderBy(branch => branch.Name)
            .Select(branch => new PublicBranchResponse(branch.Id, branch.Name, branch.Address, branch.Phone))
            .ToListAsync(token);
        var divisionRows = await db.Divisions.AsNoTracking().Where(x => x.IsActive && x.IsPublic)
            .OrderBy(x => x.Name).Select(x => new
            {
                x.Id, x.Code, x.Name, x.Description, x.Capabilities, x.BrandingJson,
                Services = x.ServiceOfferings.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new
                { s.Id, s.Code, s.Name, s.Description, s.Type, s.IsBookableOnline, s.RequiresQuote }).ToList()
            }).ToListAsync(token);
        var divisions = divisionRows.Select(x => new { x.Id, x.Code, x.Name, x.Description, x.Capabilities, x.Services, Branding = DivisionPresentation.Read(x.BrandingJson) }).ToList();
        var assetsResult = await GetAssets(null, null, null, null, null, token);
        if (assetsResult.Result is not OkObjectResult assetsOk) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var payload = new { branches, divisions, assets = assetsOk.Value };
        memoryCache?.Set(cacheKey, payload, TimeSpan.FromSeconds(30));
        Response.Headers.CacheControl = "public, max-age=30, stale-while-revalidate=60";
        return Ok(payload);
    }

    [HttpGet("assets/{assetId:guid}/photos/{fileName}")]
    [AllowAnonymous]

    public async Task<ActionResult> GetAssetPhoto(Guid assetId, string fileName, CancellationToken token)
    {
        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal)) return BadRequest();
        var storedName = safeName.EndsWith(".thumb.webp", StringComparison.OrdinalIgnoreCase)
            ? safeName[..^".thumb.webp".Length] + ".webp"
            : safeName;
        var expectedUrl = $"/api/public/assets/{assetId}/photos/{storedName}";
        var json = await db.Assets.AsNoTracking().Where(x => x.Id == assetId && x.IsActive).Select(x => x.PhotoUrlsJson).FirstOrDefaultAsync(token);
        if (json is null || !ParseJsonArray(json).Contains(expectedUrl, StringComparer.Ordinal)) return NotFound();
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "asset-images", assetId.ToString("N")));
        var path = Path.GetFullPath(Path.Combine(root, safeName));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !System.IO.File.Exists(path)) return NotFound();
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return PhysicalFile(path, contentType, enableRangeProcessing: true);
    }

    [HttpGet("divisions")]
    [AllowAnonymous]
    public async Task<ActionResult> GetDivisions(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "public, max-age=300, stale-while-revalidate=60";
        var divisions = await db.Divisions.AsNoTracking().Where(x => x.IsActive && x.IsPublic)
            .OrderBy(x => x.Name).Select(x => new
            {
                x.Id, x.Code, x.Name, x.Description, x.Capabilities, x.BrandingJson,
                Services = x.ServiceOfferings.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new
                { s.Id, s.Code, s.Name, s.Description, s.Type, s.IsBookableOnline, s.RequiresQuote }).ToList()
            }).ToListAsync(cancellationToken);
        return Ok(divisions.Select(x => new { x.Id, x.Code, x.Name, x.Description, x.Capabilities, x.Services, Branding = DivisionPresentation.Read(x.BrandingJson) }));
    }

    [HttpGet("branches")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicBranchResponse>>> GetBranches(
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "public, max-age=300, stale-while-revalidate=60";
        var branches = await db.Branches.AsNoTracking()
            .Where(branch => branch.IsActive && branch.IsPublic)
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
        // The unfiltered catalogue is safe to reuse briefly. Date-filtered
        // availability must remain fresh because bookings can change it.
        if (!startDate.HasValue && !endDate.HasValue)
            Response.Headers.CacheControl = "public, max-age=30, stale-while-revalidate=60";

        if (startDate.HasValue != endDate.HasValue ||
            startDate.HasValue && endDate <= startDate)
        {
            ModelState.AddModelError(nameof(endDate), "Choose a return date after the pickup date.");
            return ValidationProblem(ModelState);
        }
        if (startDate.HasValue && startDate < DateOnly.FromDateTime(DateTime.UtcNow))
        { ModelState.AddModelError(nameof(startDate), "Pickup cannot be in the past."); return ValidationProblem(ModelState); }

        var query = db.Assets.AsNoTracking()
            .Where(asset => asset.IsActive && asset.Branch!.IsActive && asset.Branch.IsPublic &&
                asset.Division != null && asset.Division.IsActive && asset.Division.IsPublic &&
                (asset.ServiceOffering == null || asset.ServiceOffering.IsActive && (asset.ServiceOffering.IsBookableOnline || asset.ServiceOffering.RequiresQuote) && db.BranchDivisionServices.Any(s => s.BranchId == asset.BranchId && s.DivisionId == asset.DivisionId && s.ServiceOfferingId == asset.ServiceOfferingId && s.IsActive && s.IsBookable)) &&
                db.BranchDivisions.Any(b => b.BranchId == asset.BranchId && b.DivisionId == asset.DivisionId && b.IsActive && b.AcceptsBookings) &&
                asset.Status != AssetStatus.Maintenance && asset.Status != AssetStatus.Inspection &&
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
                asset.BranchId, BranchName = asset.Branch!.Name, asset.DailyRate, asset.HireUnit, asset.UseServiceHireRate,
                asset.RegistrationNumber, asset.SerialNumber, asset.DivisionId,
                DivisionName = asset.Division != null ? asset.Division.Name : null,
                asset.Category, CategoryCode = asset.AssetCategory != null ? asset.AssetCategory.Code : null,
                asset.PersonnelOverride, asset.InheritBond, asset.DefaultBondAmount,
                DivisionBondAmount = asset.Division != null ? asset.Division.DefaultBondAmount : 0m,
                BookingConfigurationJson = asset.Division != null ? asset.Division.CustomerBookingConfigurationJson : "{}",
                ServiceName = asset.ServiceOffering != null ? asset.ServiceOffering.Name : null,
                ServiceRequiresQuote = asset.ServiceOffering != null && asset.ServiceOffering.RequiresQuote,
                asset.AssetCategoryId, asset.ServiceOfferingId, IsBookableOnline = asset.ServiceOffering == null || asset.ServiceOffering.IsBookableOnline, AllowsQuoteRequests = asset.ServiceOffering == null || asset.ServiceOffering.AllowsQuoteRequests,
                RequiresDelivery = asset.RequiresDelivery, FulfilmentMode = asset.RequiresDelivery ? AssetFulfilmentMode.DeliveryOnly : asset.FulfilmentMode,
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

        return Ok(assets.Where(asset => DivisionBookingFlow.Parse(asset.BookingConfigurationJson).OnlineRequestsEnabled).Select(asset => new PublicAssetResponse(
            asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status,
            asset.BranchId, asset.BranchName, asset.DailyRate,
            asset.RegistrationNumber, asset.SerialNumber,
            asset.DivisionId, asset.DivisionName, asset.Category, asset.CategoryCode,
            asset.PersonnelOverride ?? DivisionBookingFlow.Parse(asset.BookingConfigurationJson).PersonnelRequirement,
            asset.ServiceName, !AssetCategoryPolicy.IsVehicle(asset.Type) || asset.ServiceRequiresQuote || asset.AllowsQuoteRequests && DivisionBookingFlow.Parse(asset.BookingConfigurationJson).QuoteMode == "Always", asset.RequiresDelivery, asset.PhotoUrlsJson,
            asset.Attributes,
            startDate.HasValue
                ? asset.Status == AssetStatus.Available && !unavailableAssetIds.Contains(asset.Id)
                : asset.Status == AssetStatus.Available, asset.InheritBond ? asset.DivisionBondAmount : asset.DefaultBondAmount,
            DivisionBookingFlow.Parse(asset.BookingConfigurationJson).QuoteMode, asset.HireUnit, asset.UseServiceHireRate, asset.ServiceOfferingId, asset.IsBookableOnline, asset.AllowsQuoteRequests, asset.AssetCategoryId, asset.FulfilmentMode)).ToList());
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
            .FirstOrDefaultAsync(x => x.Id == assetId && x.IsActive && x.Branch!.IsActive && x.Branch.IsPublic &&
                x.Division != null && x.Division.IsActive && x.Division.IsPublic &&
                (x.ServiceOffering == null || x.ServiceOffering.IsActive && (x.ServiceOffering.IsBookableOnline || x.ServiceOffering.RequiresQuote) && db.BranchDivisionServices.Any(s => s.BranchId == x.BranchId && s.DivisionId == x.DivisionId && s.ServiceOfferingId == x.ServiceOfferingId && s.IsActive && s.IsBookable)) &&
                db.BranchDivisions.Any(b => b.BranchId == x.BranchId && b.DivisionId == x.DivisionId && b.IsActive && b.AcceptsBookings) &&
                x.Status != AssetStatus.Maintenance && x.Status != AssetStatus.Inspection && x.Status != AssetStatus.OutOfService &&
                x.Status != AssetStatus.Retired, cancellationToken);
        if (asset is null || !DivisionBookingFlow.For(asset.Division).OnlineRequestsEnabled) return NotFound();

        var isAvailable = asset.Status == AssetStatus.Available;
        if (startDate.HasValue && endDate.HasValue)
        {
            var start = new DateTimeOffset(startDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset(endDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            isAvailable = asset.Status == AssetStatus.Available && !await db.BookingItems.AsNoTracking().AnyAsync(item => item.AssetId == asset.Id &&
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
        var personnelAllowed = AssetCategoryPolicy.AllowsPersonnel(asset);
        var visibleCharges = await db.ChargeDefinitions.AsNoTracking()
            .Where(x => x.IsActive && x.IsCustomerVisible && x.Category != ChargeCategory.BaseHire &&
                x.DivisionId == asset.DivisionId &&
                (!x.ServiceOfferingId.HasValue || x.ServiceOfferingId == asset.ServiceOfferingId))
            .Where(x => personnelAllowed || x.Category != ChargeCategory.Driver && x.Category != ChargeCategory.Operator)
            .OrderBy(x => x.Category).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Category, x.Unit, x.DefaultSellingRate, x.IsRequired, x.IsTaxable })
            .ToListAsync(cancellationToken);

        HireRate hireRate;
        try { hireRate = await ConfiguredHirePricing.Resolve(db, asset, cancellationToken); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
        return Ok(new
        {
            hireRate,
            asset.Id, asset.Name, asset.Type, asset.Category, asset.DailyRate, PersonnelRequirement = AssetCategoryPolicy.Personnel(asset),
            BondAmount = AssetCategoryPolicy.Bond(asset), TaxRate = asset.Division?.DefaultTaxRate ?? FijiRentalDefaults.VatRate,
            BookingFlow = DivisionBookingFlow.For(asset.Division),
            CustomerQuestions = OrganizationConfiguration.Questions(DivisionBookingFlow.For(asset.Division), asset.ServiceOffering, asset.AssetCategory),
            asset.Manufacturer, asset.Model, asset.ModelYear, asset.PhotoUrlsJson,
            Division = asset.Division is null ? null : new { asset.Division.Id, asset.Division.Code, asset.Division.Name },
            Branch = new { asset.BranchId, asset.Branch!.Name, asset.Branch.Address, asset.Branch.Phone,
                asset.Branch.Email, asset.Branch.PickupInstructions, asset.Branch.DeliveryCoverage },
            Service = asset.ServiceOffering is null ? null : new
            {
                asset.ServiceOffering.Id, asset.ServiceOffering.Code, asset.ServiceOffering.Name,
                asset.ServiceOffering.Description, asset.ServiceOffering.Type,
                PersonnelRequirement = AssetCategoryPolicy.Personnel(asset),
                RequiresQuote = asset.ServiceOffering.RequiresQuote || asset.ServiceOffering.AllowsQuoteRequests && DivisionBookingFlow.For(asset.Division).QuoteMode == "Always", asset.ServiceOffering.IsBookableOnline, asset.ServiceOffering.AllowsQuoteRequests, asset.ServiceOffering.DefaultHireUnit,
                RequiresDelivery = asset.RequiresDelivery, FulfilmentMode = PublicRentalRequestPolicy.Fulfilment(asset), DefaultDepositAmount = AssetCategoryPolicy.Bond(asset),
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
        var asset = await db.Assets.Include(item => item.Branch).Include(item => item.Division)
            .Include(item => item.ServiceOffering).Include(item => item.AssetCategory)
            .FirstOrDefaultAsync(item => item.Id == request.AssetId && item.IsActive, cancellationToken);
        if (asset is null || asset.Branch is null || !asset.Branch.IsActive || !asset.Branch.IsPublic ||
            asset.Division is not { IsActive: true, IsPublic: true } ||
            asset.ServiceOffering is { IsActive: false } ||
            asset.ServiceOffering is { IsBookableOnline: false, RequiresQuote: false } ||
            asset.Status is AssetStatus.Rented or AssetStatus.Maintenance or AssetStatus.Inspection or AssetStatus.OutOfService or AssetStatus.Retired)
            return NotFound("The selected rental item is no longer available.");
        if (request.RequestQuotation && asset.ServiceOffering?.AllowsQuoteRequests == false) return BadRequest(new { message = "This service accepts booking requests only." });
        var flow = DivisionBookingFlow.For(asset.Division);
        if (!flow.OnlineRequestsEnabled) return NotFound("Online requests are disabled for this division.");
        var questions = OrganizationConfiguration.Questions(flow, asset.ServiceOffering, asset.AssetCategory);
        var answerError = OrganizationConfiguration.ValidateAnswers(questions, request.ConfigurationAnswers);
        if (answerError is not null) return BadRequest(new { message = answerError });
        var requestedDays = request.EndDate.DayNumber - request.StartDate.DayNumber;
        if (requestedDays < flow.MinimumHireDays || requestedDays > flow.MaximumHireDays)
        { ModelState.AddModelError(nameof(request.EndDate), $"Choose a hire between {flow.MinimumHireDays} and {flow.MaximumHireDays} days."); return ValidationProblem(ModelState); }
        if (request.Fulfilment is not ("Pickup" or "Delivery"))
        { ModelState.AddModelError(nameof(request.Fulfilment), "Choose pickup or delivery."); return ValidationProblem(ModelState); }
        var fulfilmentMode = PublicRentalRequestPolicy.Fulfilment(asset);
        if (fulfilmentMode == AssetFulfilmentMode.PickupOnly && request.Fulfilment == "Delivery")
            return BadRequest(new { message = "This asset is available for branch pickup only." });
        var deliveryRequired = fulfilmentMode == AssetFulfilmentMode.DeliveryOnly;
        if (deliveryRequired &&
            !string.Equals(request.Fulfilment, "Delivery", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(request.Fulfilment), "This rental must be delivered to the worksite.");
            return ValidationProblem(ModelState);
        }
        if (request.Fulfilment == "Delivery" && string.IsNullOrWhiteSpace(request.DeliveryAddress))
        {
            ModelState.AddModelError(nameof(request.DeliveryAddress), "Enter the delivery or worksite address.");
            return ValidationProblem(ModelState);
        }
        if (!await db.BranchDivisions.AnyAsync(x => x.BranchId == asset.BranchId && x.DivisionId == asset.DivisionId && x.IsActive && x.AcceptsBookings, cancellationToken) ||
            asset.ServiceOfferingId.HasValue && !await db.BranchDivisionServices.AnyAsync(x => x.BranchId == asset.BranchId && x.DivisionId == asset.DivisionId && x.ServiceOfferingId == asset.ServiceOfferingId && x.IsActive && x.IsBookable, cancellationToken))
            return BadRequest(new { message = "This service is not currently bookable at the selected branch." });

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
        var licenceNumber = await db.CustomerLicences.AsNoTracking()
            .Where(x => x.CustomerId == activeCustomer.Id && x.Status == LicenceVerificationStatus.Verified)
            .OrderByDescending(x => x.ConfirmedAt).Select(x => x.LicenceNumber).FirstOrDefaultAsync(cancellationToken);
        var professionalPersonnelProvided = AssetCategoryPolicy.RequiresPersonnel(asset) || request.PersonnelRequested;
        var licenceRequired = flow.RequireVerifiedLicenceForVehicles &&
            AssetCategoryPolicy.RequiresDrivingLicence(asset, professionalPersonnelProvided);
        if (licenceRequired && string.IsNullOrWhiteSpace(licenceNumber))
        {
            ModelState.AddModelError(nameof(request.DriverLicence), "Verify your driver licence in My account before requesting this rental.");
            return ValidationProblem(ModelState);
        }
        request = request with
        {
            CustomerType = activeCustomer.Type,
            CompanyName = activeCustomer.Type == CustomerType.Business ? activeCustomer.Name : null,
            FullName = activeCustomer.Type == CustomerType.Business && !string.IsNullOrWhiteSpace(signedInUser.FullName)
                ? signedInUser.FullName : activeCustomer.Name,
            Email = activeCustomer.Email ?? request.Email,
            Phone = activeCustomer.Phone ?? request.Phone,
            DriverName = licenceRequired ? activeCustomer.Name : request.DriverName,
            DriverLicence = licenceRequired ? licenceNumber : request.DriverLicence,
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Keep the registered identity authoritative while allowing current contact details.
        // Contact identity is read from the customer profile; checkout does not overwrite it.
        activeCustomer.Address = Normalize(request.Address) ?? activeCustomer.Address;
        if (!licenceRequired)
            activeCustomer.IdentificationNumber = Normalize(request.IdentificationNumber) ?? activeCustomer.IdentificationNumber;

        var hireDays = Math.Max(1, request.EndDate.DayNumber - request.StartDate.DayNumber);
        var availableCharges = await db.ChargeDefinitions.AsNoTracking().Where(x => x.IsActive && x.IsCustomerVisible && x.Category != ChargeCategory.BaseHire &&
            x.DivisionId == asset.DivisionId && (!x.ServiceOfferingId.HasValue || x.ServiceOfferingId == asset.ServiceOfferingId))
            .ToListAsync(cancellationToken);
        var personnelPolicy = AssetCategoryPolicy.Personnel(asset);
        if (request.PersonnelRequested && !AssetCategoryPolicy.AllowsPersonnel(asset))
            return BadRequest(new { message = "A driver or operator is not applicable to this asset category." });
        var personnelRequested = personnelPolicy == PersonnelRequirement.Required || request.PersonnelRequested;
        if (personnelRequested && request.PersonnelHours is <= 0 or > 1000)
        { ModelState.AddModelError(nameof(request.PersonnelHours), "Enter between 1 and 1000 driver or operator hours."); return ValidationProblem(ModelState); }
        var pricedPersonnelAvailable = availableCharges.Any(x =>
            (x.Category is ChargeCategory.Operator or ChargeCategory.Driver) && x.DefaultSellingRate > 0);
        var pricedTransportAvailable = availableCharges.Any(x =>
            x.Category == ChargeCategory.Transport && x.DefaultSellingRate > 0 && x.Unit != ChargeUnit.Hour);
        var requiresQuote = PublicRentalRequestPolicy.RequiresQuotation(asset, request.Fulfilment == "Delivery", personnelRequested) || asset.ServiceOffering?.RequiresQuote == true || asset.ServiceOffering?.AllowsQuoteRequests != false && (
            (!asset.UseServiceHireRate && asset.DailyRate <= 0) || flow.RequiresQuote(request.RequestQuotation,
            activeCustomer.Type == CustomerType.Business, personnelPolicy, personnelRequested,
            pricedPersonnelAvailable, request.Fulfilment == "Delivery", pricedTransportAvailable));
        if (requiresQuote && asset.ServiceOffering?.AllowsQuoteRequests == false)
            return BadRequest(new { message = "This service cannot accept quotation requests online. Contact the branch to arrange delivery or personnel." });
        var selectedExtras = (request.Extras ?? []).Where(x => x.Quantity > 0 && x.Quantity <= 1000)
            .GroupBy(x => x.ChargeDefinitionId).ToDictionary(x => x.Key, x => x.First().Quantity);
        var applicableCharges = availableCharges.Where(x => x.Category is not (ChargeCategory.Operator or ChargeCategory.Driver or ChargeCategory.Transport))
            .Where(x => !(requiresQuote && asset.RequiresDelivery && x.Category == ChargeCategory.Transport) && (x.IsRequired || selectedExtras.ContainsKey(x.Id) ||
                request.Fulfilment == "Delivery" && x.Category == ChargeCategory.Transport ||
                personnelRequested && x.Category is ChargeCategory.Operator or ChargeCategory.Driver))
            .ToList();
        if (applicableCharges.Any(x => HirePricing.IsMeasured(x.Unit) && !selectedExtras.ContainsKey(x.Id)))
            return BadRequest(new { message = "Enter a quantity for each selected per-unit, trip, distance, weight or area charge." });
        var charges = applicableCharges.Select(x => new {
            Definition = x,
            Quantity = x.Unit == ChargeUnit.Hour && x.Category is ChargeCategory.Operator or ChargeCategory.Driver
                ? Math.Max(1, request.PersonnelHours ?? hireDays * 8)
                : HirePricing.Quantity(x.Unit,start,end,selectedExtras.GetValueOrDefault(x.Id))
        }).ToList();
        HireRate hireRate;
        decimal hireQuantity;
        try {
            hireRate = await ConfiguredHirePricing.Resolve(db, asset, cancellationToken);
            hireQuantity = hireRate.Unit == ChargeUnit.Hour && request.HireQuantity is > 0
                ? request.HireQuantity.Value
                : HirePricing.Quantity(hireRate.Unit, start, end, request.HireQuantity);
            if (hireRate.Unit == ChargeUnit.Hour && (hireQuantity != decimal.Truncate(hireQuantity) || hireQuantity > (decimal)(end - start).TotalHours)) throw new ArgumentException("Requested hire hours must fit within the selected dates.");
        } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return BadRequest(new { message = error.Message }); }
        if (asset.ServiceOffering?.AllowsQuoteRequests == false && hireRate.Rate <= 0) return BadRequest(new { message = "Set a hire rate before accepting booking requests for this service." });
        var baseSubtotal = decimal.Round(hireQuantity * hireRate.Rate, 2, MidpointRounding.AwayFromZero);
        var chargeSubtotal = charges.Sum(x => x.Quantity * x.Definition.DefaultSellingRate);
        var taxRate = asset.Division?.DefaultTaxRate ?? FijiRentalDefaults.VatRate;
        var tax = decimal.Round(((hireRate.IsTaxable ? baseSubtotal : 0) + charges.Where(x => x.Definition.IsTaxable).Sum(x => x.Quantity * x.Definition.DefaultSellingRate)) * taxRate / 100m, 2);
        var total = baseSubtotal + chargeSubtotal + tax;
        var booking = new Booking
        {
            BookingNumber = requiresQuote
                ? $"REQ-{Guid.NewGuid():N}"[..16].ToUpperInvariant()
                : await RentalSettings.Reference(db, false, cancellationToken),
            CustomerId = activeCustomer.Id, BranchId = asset.BranchId, Status = BookingStatus.Draft,
            ConfigurationSnapshotJson = JsonSerializer.Serialize(await ConfiguredHireRules.Capture(db, asset, request.ConfigurationAnswers, cancellationToken), OrganizationConfiguration.Json),
            Notes = BuildRequestNotes(request, personnelRequested) + (requiresQuote ? "\nRequest type: Quotation." : "\nRequest type: Booking."),
            TaxRate = taxRate, DepositRequired = AssetCategoryPolicy.Bond(asset),
            BondStatus = AssetCategoryPolicy.Bond(asset) > 0 ? BondStatus.AwaitingPayment : BondStatus.NotRequired,
            AdditionalCharges = chargeSubtotal,
            AdditionalChargesDescription = charges.Count == 0 ? null : string.Join(", ", charges.Select(x => x.Definition.Name)),
            Items = [new BookingItem { AssetId = asset.Id, StartAt = start, EndAt = end, DailyRate = hireRate.Rate, HireUnit = hireRate.Unit, HireQuantity = hireQuantity, HireIsTaxable = hireRate.IsTaxable }],
        };
        foreach (var charge in charges)
            booking.Charges.Add(new BookingCharge { AssetId = asset.Id, ChargeDefinitionId = charge.Definition.Id,
                Description = charge.Definition.Name, Category = charge.Definition.Category, Unit = charge.Definition.Unit,
                Quantity = charge.Quantity, UnitRate = charge.Definition.DefaultSellingRate,
                UnitCost = charge.Definition.DefaultCostRate, IsTaxable = charge.Definition.IsTaxable });
        db.Bookings.Add(booking);

        if (requiresQuote)
        {
            var lineItems = new List<QuoteLine> { new($"{asset.Name} hire", hireQuantity, hireRate.Rate, hireRate.Unit, Category: ChargeCategory.BaseHire, IsTaxable: hireRate.IsTaxable) };
            lineItems.AddRange(charges.Select(x => new QuoteLine(x.Definition.Name, x.Quantity,
                x.Definition.DefaultSellingRate, x.Definition.Unit, Category: x.Definition.Category, ChargeDefinitionId: x.Definition.Id, IsTaxable: x.Definition.IsTaxable)));
            if (request.Fulfilment == "Delivery") lineItems.Add(new QuoteLine("Delivery — charge to be confirmed", 1, 0, ChargeUnit.Trip, Category: ChargeCategory.Transport));
            if (personnelRequested) lineItems.Add(new QuoteLine("Operator / driver — charge to be confirmed", request.PersonnelHours ?? 1, 0, ChargeUnit.Hour, Category: ChargeCategory.Operator));
            var quote = new SalesQuote
            {
                QuoteNumber = $"QUO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                CustomerId = activeCustomer.Id, BranchId = asset.BranchId, DivisionId = asset.DivisionId,
                Status = QuoteStatus.Draft, ValidUntil = DateTimeOffset.UtcNow.AddDays(flow.QuoteValidityDays),
                JobSite = Normalize(request.DeliveryAddress), PurchaseOrderNumber = Normalize(request.PurchaseOrderNumber),
                Subtotal = baseSubtotal + chargeSubtotal, Tax = tax, Total = total,
                LineItemsJson = QuoteLineSerialization.Serialize(lineItems), ConvertedBookingId = booking.Id,
            };
            db.SalesQuotes.Add(quote);
            db.CustomerCases.Add(new CustomerCase { CaseNumber = $"CASE-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                CustomerId = activeCustomer.Id, BranchId = asset.BranchId, Type = CaseType.Enquiry,
                Priority = CasePriority.Normal, Subject = $"Online quotation request — {quote.QuoteNumber}",
                Description = BuildRequestNotes(request, personnelRequested), DueAt = DateTimeOffset.UtcNow.AddHours(8) });
            QueueCustomerConfirmation(signedInUser, quote.QuoteNumber, asset.Name, request.StartDate, request.EndDate,
                "Quotation request received", "Our team will review availability, transport, personnel and final charges. You can follow the quotation in your customer account.");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Ok(new PublicBookingResponse(quote.QuoteNumber, asset.Name, asset.Branch.Name,
                request.StartDate, request.EndDate, "Quotation", "Quotation request received"));
        }

        if (request.Fulfilment == "Delivery")
            db.DispatchJobs.Add(new DispatchJob { DispatchNumber = $"DSP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                BookingId = booking.Id, BranchId = booking.BranchId, Type = DispatchType.Delivery,
                ScheduledAt = start, Address = Normalize(request.DeliveryAddress), Status = DispatchStatus.Scheduled,
                DeliveryCharge = charges.Where(x => x.Definition.Category == ChargeCategory.Transport).Sum(x => x.Quantity * x.Definition.DefaultSellingRate) });
        QueueCustomerConfirmation(signedInUser, booking.BookingNumber, asset.Name, request.StartDate, request.EndDate,
            "Booking request received", "The branch will verify your details and confirm the booking. You can follow pickup requirements in your customer account.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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
        if (!string.IsNullOrWhiteSpace(request.SecondaryContact)) details.Add($"Emergency / secondary contact: {request.SecondaryContact.Trim()}");
        if (request.Fulfilment == "Delivery") details.Add("Delivery charge will be confirmed in the quotation.");
        if (personnelRequested) details.Add("Operator / driver charges will be confirmed in the quotation.");
        if (request.CustomerType == CustomerType.Business && !string.IsNullOrWhiteSpace(request.CompanyName))
            details.Add($"Registered business: {request.CompanyName.Trim()}");
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
    string? DivisionName, string? Category, string? CategoryCode, PersonnelRequirement PersonnelRequirement,
    string? ServiceName, bool RequiresQuote, bool RequiresDelivery, string PhotoUrlsJson,
    IReadOnlyList<PublicAssetAttributeResponse> Attributes, bool IsAvailable, decimal BondAmount, string QuoteMode,
    ChargeUnit HireUnit = ChargeUnit.Day, bool UseServiceHireRate = false, Guid? ServiceOfferingId = null, bool IsBookableOnline = true, bool AllowsQuoteRequests = true, Guid? AssetCategoryId = null, AssetFulfilmentMode FulfilmentMode = AssetFulfilmentMode.PickupOrDelivery);
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
    bool RequestQuotation,
    IReadOnlyList<PublicBookingExtraRequest>? Extras, decimal? HireQuantity = null,
    IReadOnlyDictionary<string, string>? ConfigurationAnswers = null, [MaxLength(100)] string? SecondaryContact = null);
public sealed record PublicBookingExtraRequest(Guid ChargeDefinitionId, decimal Quantity);
public sealed record PublicBookingResponse(
    string Reference, string AssetName, string BranchName,
    DateOnly StartDate, DateOnly EndDate, string RequestType, string Status);
public sealed record PublicBookingStatusResponse(
    string Reference, string Status, string Message, string? AssetName,
    string BranchName, DateTimeOffset? StartAt, DateTimeOffset? EndAt,
    DateTimeOffset SubmittedAt);
