using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/assets")]
[Authorize(Policy = SystemPolicies.ViewAssets)]
public sealed class AssetsController(ApplicationDbContext db, CurrentStaffScope staffScope, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AssetResponse>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] Guid? divisionId,
        [FromQuery] Guid? branchId,
        [FromQuery] string? category,
        [FromQuery] AssetStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var query = db.Assets.AsNoTracking();
        if (!scope.IsAdministrator) query = query.Where(asset => scope.BranchIds.Contains(asset.BranchId) && asset.DivisionId.HasValue && scope.DivisionIds.Contains(asset.DivisionId.Value));
        if (divisionId.HasValue) query = query.Where(asset => asset.DivisionId == divisionId);
        if (branchId.HasValue) query = query.Where(asset => asset.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(asset => asset.Category == category.Trim());
        if (status.HasValue) query = query.Where(asset => asset.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(asset =>
                asset.AssetNumber.Contains(term) || asset.Name.Contains(term) ||
                (asset.RegistrationNumber != null && asset.RegistrationNumber.Contains(term)) ||
                (asset.SerialNumber != null && asset.SerialNumber.Contains(term)) ||
                (asset.Manufacturer != null && asset.Manufacturer.Contains(term)) ||
                (asset.Model != null && asset.Model.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        Response.Headers.Append("X-Total-Count", totalCount.ToString());

        var assets = await query
            .OrderBy(asset => asset.AssetNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(asset => new AssetResponse(
                asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status,
                asset.DivisionId, asset.Division != null ? asset.Division.Name : null,
                asset.BranchId, asset.Branch!.Name, asset.RegistrationNumber,
                asset.SerialNumber, asset.Category, asset.Manufacturer, asset.Model, asset.ModelYear,
                asset.VinOrChassisNumber, asset.EngineNumber, asset.MeterUnit, asset.CurrentMeterReading,
                asset.AcquisitionDate, asset.AcquisitionCost, asset.CurrentBookValue, asset.OwnershipType,
                asset.InsurancePolicyNumber, asset.InsuranceExpiry, asset.WarrantyExpiry,
                asset.DailyRate, asset.DefaultBondAmount, asset.NextServiceDate, asset.IsActive, asset.ServiceOfferingId, asset.AssetCategoryId, asset.CurrentLocation, asset.PhotoUrlsJson))
            .ToListAsync(cancellationToken);
        return Ok(assets);
    }

    [HttpGet("summary")]
    public async Task<ActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && scope.BranchIds.Count == 0)) return Forbid();
        var query = db.Assets.AsNoTracking().Where(asset => asset.IsActive);
        if (!scope.IsAdministrator) query = query.Where(asset => scope.BranchIds.Contains(asset.BranchId) && asset.DivisionId.HasValue && scope.DivisionIds.Contains(asset.DivisionId.Value));
        var counts = await query.GroupBy(asset => asset.Status).Select(group => new { Status = group.Key, Count = group.Count() }).ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var categories = await query.Where(asset => asset.Category != null).Select(asset => asset.Category!).Distinct().OrderBy(value => value).ToListAsync(cancellationToken);
        return Ok(new {
            total = await query.CountAsync(cancellationToken),
            available = counts.GetValueOrDefault(AssetStatus.Available),
            onHire = counts.GetValueOrDefault(AssetStatus.Rented),
            reserved = counts.GetValueOrDefault(AssetStatus.Reserved),
            maintenance = counts.GetValueOrDefault(AssetStatus.Maintenance),
            inspection = counts.GetValueOrDefault(AssetStatus.Inspection),
            outOfService = counts.GetValueOrDefault(AssetStatus.OutOfService),
            categories
        });
    }

    [HttpPost]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult<AssetResponse>> Create(
        SaveAssetRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(request.BranchId, request.DivisionId)) return Forbid();
        var assetNumber = request.AssetNumber.Trim().ToUpperInvariant();
        if (await db.Assets.AnyAsync(asset => asset.AssetNumber == assetNumber, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.AssetNumber), "An asset with this number already exists.");
            return ValidationProblem(ModelState);
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.BranchId && item.IsActive, cancellationToken);
        if (branch is null)
        {
            ModelState.AddModelError(nameof(request.BranchId), "Select an active branch.");
            return ValidationProblem(ModelState);
        }
        if (!request.DivisionId.HasValue || !await db.BranchDivisions.AnyAsync(x => x.BranchId == request.BranchId && x.DivisionId == request.DivisionId && x.IsActive, cancellationToken))
        { ModelState.AddModelError(nameof(request.DivisionId), "Select a division operating at this branch."); return ValidationProblem(ModelState); }
        if (!await ValidClassification(request, cancellationToken)) { ModelState.AddModelError(nameof(request.AssetCategoryId), "The service and asset category must belong to the selected division and be enabled at this branch."); return ValidationProblem(ModelState); }

        var asset = new Asset
        {
            AssetNumber = assetNumber,
            Name = request.Name.Trim(),
            Type = request.Type,
            Status = request.Status,
            DivisionId = request.DivisionId,
            ServiceOfferingId = request.ServiceOfferingId,
            AssetCategoryId = request.AssetCategoryId,
            BranchId = branch.Id,
            RegistrationNumber = Normalize(request.RegistrationNumber),
            SerialNumber = Normalize(request.SerialNumber),
            Category = Normalize(request.Category), Manufacturer = Normalize(request.Manufacturer), Model = Normalize(request.Model), ModelYear = request.ModelYear,
            VinOrChassisNumber = Normalize(request.VinOrChassisNumber), EngineNumber = Normalize(request.EngineNumber), MeterUnit = Normalize(request.MeterUnit), CurrentMeterReading = request.CurrentMeterReading,
            AcquisitionDate = request.AcquisitionDate, AcquisitionCost = request.AcquisitionCost, CurrentBookValue = request.CurrentBookValue,
            OwnershipType = Normalize(request.OwnershipType), InsurancePolicyNumber = Normalize(request.InsurancePolicyNumber), InsuranceExpiry = request.InsuranceExpiry, WarrantyExpiry = request.WarrantyExpiry,
            DailyRate = request.DailyRate,
            DefaultBondAmount = request.DefaultBondAmount,
            NextServiceDate = request.NextServiceDate,
            CurrentLocation = Normalize(request.CurrentLocation) ?? branch.Name,
            PhotoUrlsJson = request.PhotoUrlsJson ?? "[]",
            IsActive = true,
        };
        db.Assets.Add(asset);
        AuditWriter.Record(db, scope, "Asset created", "Asset", asset.Id,
            $"{asset.AssetNumber} was added to {branch.Name}.", asset.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetAll), ToResponse(asset, branch.Name));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult<AssetResponse>> Update(
        Guid id,
        SaveAssetRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(request.BranchId, request.DivisionId)) return Forbid();
        var asset = await db.Assets.FindAsync([id], cancellationToken);
        if (asset is null) return NotFound();
        if (!scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();

        var assetNumber = request.AssetNumber.Trim().ToUpperInvariant();
        if (await db.Assets.AnyAsync(
            other => other.Id != id && other.AssetNumber == assetNumber, cancellationToken))
        {
            ModelState.AddModelError(nameof(request.AssetNumber), "An asset with this number already exists.");
            return ValidationProblem(ModelState);
        }

        var branch = await db.Branches.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.BranchId && item.IsActive, cancellationToken);
        if (branch is null)
        {
            ModelState.AddModelError(nameof(request.BranchId), "Select an active branch.");
            return ValidationProblem(ModelState);
        }
        if (!request.DivisionId.HasValue || !await db.BranchDivisions.AnyAsync(x => x.BranchId == request.BranchId && x.DivisionId == request.DivisionId && x.IsActive, cancellationToken))
        { ModelState.AddModelError(nameof(request.DivisionId), "Select a division operating at this branch."); return ValidationProblem(ModelState); }
        if (!await ValidClassification(request, cancellationToken)) { ModelState.AddModelError(nameof(request.AssetCategoryId), "The service and asset category must belong to the selected division and be enabled at this branch."); return ValidationProblem(ModelState); }

        asset.AssetNumber = assetNumber;
        asset.Name = request.Name.Trim();
        asset.Type = request.Type;
        asset.Status = request.Status;
        asset.DivisionId = request.DivisionId;
        asset.ServiceOfferingId = request.ServiceOfferingId;
        asset.AssetCategoryId = request.AssetCategoryId;
        asset.BranchId = branch.Id;
        asset.RegistrationNumber = Normalize(request.RegistrationNumber);
        asset.SerialNumber = Normalize(request.SerialNumber);
        asset.Category = Normalize(request.Category); asset.Manufacturer = Normalize(request.Manufacturer); asset.Model = Normalize(request.Model); asset.ModelYear = request.ModelYear;
        asset.VinOrChassisNumber = Normalize(request.VinOrChassisNumber); asset.EngineNumber = Normalize(request.EngineNumber); asset.MeterUnit = Normalize(request.MeterUnit); asset.CurrentMeterReading = request.CurrentMeterReading;
        asset.AcquisitionDate = request.AcquisitionDate; asset.AcquisitionCost = request.AcquisitionCost; asset.CurrentBookValue = request.CurrentBookValue; asset.OwnershipType = Normalize(request.OwnershipType);
        asset.InsurancePolicyNumber = Normalize(request.InsurancePolicyNumber); asset.InsuranceExpiry = request.InsuranceExpiry; asset.WarrantyExpiry = request.WarrantyExpiry;
        asset.DailyRate = request.DailyRate;
        asset.DefaultBondAmount = request.DefaultBondAmount;
        asset.NextServiceDate = request.NextServiceDate;
        asset.CurrentLocation = Normalize(request.CurrentLocation) ?? branch.Name;
        asset.PhotoUrlsJson = request.PhotoUrlsJson ?? "[]";
        AuditWriter.Record(db, scope, "Asset updated", "Asset", asset.Id,
            $"{asset.AssetNumber} details were updated.", asset.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(asset, branch.Name));
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> SetStatus(
        Guid id,
        SetAssetStatusRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FindAsync([id], cancellationToken);
        if (asset is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        asset.Status = request.Status;
        asset.IsActive = request.IsActive;
        AuditWriter.Record(db, scope, "Asset status changed", "Asset", asset.Id,
            $"{asset.AssetNumber} changed to {request.Status}.", asset.BranchId);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/photos")]
    [Authorize(Roles = "SuperAdministrator,Administrator,BranchManager,RentalOfficer")]
    [RequestSizeLimit(5_500_000)]
    public async Task<ActionResult> UploadPhoto(Guid id, [FromForm] IFormFile file, CancellationToken token)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, token);
        if (asset is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        if (file.Length is <= 0 or > 5_242_880) return BadRequest(new { message = "Choose a JPEG, PNG or WebP image no larger than 5 MB." });
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp")) return BadRequest(new { message = "Only JPEG, PNG and WebP asset photos are accepted." });
        await using var source = new MemoryStream();
        await file.CopyToAsync(source, token);
        var bytes = source.ToArray();
        if (!HasValidImageSignature(extension, bytes)) return BadRequest(new { message = "The image content does not match its file extension." });

        var urls = ParsePhotoUrls(asset.PhotoUrlsJson);
        if (urls.Count >= 8) return BadRequest(new { message = "An asset can have up to eight photos. Remove an existing photo first." });
        var storageName = $"{Guid.NewGuid():N}{extension}";
        var root = Path.Combine(environment.ContentRootPath, "App_Data", "asset-images", asset.Id.ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, storageName);
        await System.IO.File.WriteAllBytesAsync(path, bytes, token);
        var url = $"/api/public/assets/{asset.Id}/photos/{storageName}";
        urls.Add(url);
        asset.PhotoUrlsJson = JsonSerializer.Serialize(urls);
        AuditWriter.Record(db, scope, "Asset photo uploaded", "Asset", asset.Id, $"A catalogue photo was added to {asset.AssetNumber}.", asset.BranchId);
        await db.SaveChangesAsync(token);
        return Ok(new { url, photoUrls = urls });
    }

    [HttpDelete("{id:guid}/photos/{fileName}")]
    [Authorize(Roles = "SuperAdministrator,Administrator,BranchManager,RentalOfficer")]
    public async Task<ActionResult> DeletePhoto(Guid id, string fileName, CancellationToken token)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, token);
        if (asset is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal)) return BadRequest();
        var url = $"/api/public/assets/{asset.Id}/photos/{safeName}";
        var urls = ParsePhotoUrls(asset.PhotoUrlsJson);
        if (!urls.Remove(url)) return NotFound();
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "asset-images", asset.Id.ToString("N"), safeName);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        asset.PhotoUrlsJson = JsonSerializer.Serialize(urls);
        AuditWriter.Record(db, scope, "Asset photo removed", "Asset", asset.Id, $"A catalogue photo was removed from {asset.AssetNumber}.", asset.BranchId);
        await db.SaveChangesAsync(token);
        return Ok(new { photoUrls = urls });
    }

    [HttpGet("{id:guid}/performance")]
    [Authorize(Policy = SystemPolicies.ViewReports)]
    public async Task<ActionResult> Performance(Guid id, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.AsNoTracking().Include(x => x.Branch).Include(x => x.Division).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (asset is null) return NotFound();
        var scope = await staffScope.GetAsync(User); if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        var items = await db.BookingItems.AsNoTracking().Where(x => x.AssetId == id && x.Booking != null && (x.Booking.Status == CREMS.Api.Domain.Rentals.BookingStatus.Completed || x.Booking.Status == CREMS.Api.Domain.Rentals.BookingStatus.ConvertedToRental))
            .Select(x => new { x.StartAt, x.EndAt, x.DailyRate, x.BookingId, x.Booking!.DiscountAmount, x.Booking.AdditionalCharges, ItemCount = x.Booking.Items.Count }).ToListAsync(cancellationToken);
        var bookingIds = items.Select(x => x.BookingId).Distinct().ToList();
        var componentCharges = await db.BookingCharges.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId) && (x.AssetId == id || x.AssetId == null)).ToListAsync(cancellationToken);
        var componentRevenue = componentCharges.Sum(x => x.Quantity * x.UnitRate); var componentExpense = componentCharges.Sum(x => x.Quantity * x.UnitCost);
        var recordedComponentRevenue = componentCharges.GroupBy(x => x.BookingId).ToDictionary(x => x.Key, x => x.Sum(c => c.Quantity * c.UnitRate));
        var rentalRevenue = items.Sum(x => Math.Max(1, (decimal)Math.Ceiling((x.EndAt - x.StartAt).TotalDays)) * x.DailyRate + (x.ItemCount == 1 ? Math.Max(0, x.AdditionalCharges - recordedComponentRevenue.GetValueOrDefault(x.BookingId)) - x.DiscountAmount : 0)) + componentRevenue;
        var maintenance = await db.MaintenanceJobs.AsNoTracking().Where(x => x.AssetId == id).OrderByDescending(x => x.ReportedAt).ToListAsync(cancellationToken);
        var costs = await db.AssetCostEntries.AsNoTracking().Where(x => x.AssetId == id).OrderByDescending(x => x.OccurredOn).ToListAsync(cancellationToken);
        var transferCost = await db.AssetTransfers.AsNoTracking().Where(x => x.AssetId == id && (x.Status == CREMS.Api.Domain.Corporate.TransferStatus.Received || x.Status == CREMS.Api.Domain.Corporate.TransferStatus.Inspected)).SumAsync(x => (decimal?)x.TransferCost, cancellationToken) ?? 0;
        var maintenanceExpense = maintenance.Where(x => x.Status != MaintenanceStatus.Cancelled).Sum(x => x.ActualCost ?? 0);
        var operatingExpense = costs.Sum(x => x.Amount) + componentExpense; var totalExpense = maintenanceExpense + operatingExpense + transferCost;
        var inspectionCount = await db.RentalInspections.CountAsync(x => x.Booking!.Items.Any(i => i.AssetId == id), cancellationToken);
        return Ok(new { asset.Id, asset.AssetNumber, asset.Name, asset.Category, asset.Manufacturer, asset.Model, asset.ModelYear, asset.Status, branchName = asset.Branch!.Name, divisionName = asset.Division?.Name,
            rentalRevenue, maintenanceExpense, operatingExpense, transferExpense = transferCost, totalExpense, operatingProfit = rentalRevenue - totalExpense,
            asset.AcquisitionCost, asset.CurrentBookValue, lifetimeNetAfterAcquisition = rentalRevenue - totalExpense - asset.AcquisitionCost,
            rentalCount = items.Select(x => x.BookingId).Distinct().Count(), rentalDays = items.Sum(x => Math.Max(1, Math.Ceiling((x.EndAt - x.StartAt).TotalDays))), inspectionCount,
            maintenance = maintenance.Select(x => new { x.Id, x.JobNumber, x.ServiceType, x.Status, x.ReportedAt, x.CompletedAt, x.ActualCost, x.PartsCost, x.LabourCost, x.TransportCost, x.ExternalServiceCost, x.TaxCost, x.OtherCost, x.DowntimeHours, x.InvoiceNumber }),
            chargeComponents = componentCharges.Select(x => new { x.Id, x.Description, x.Category, x.Unit, x.Quantity, x.UnitRate, x.UnitCost, revenue = x.Quantity * x.UnitRate, expense = x.Quantity * x.UnitCost }),
            costs = costs.Select(x => new { x.Id, x.Category, x.Description, x.Amount, x.OccurredOn, x.Supplier, x.ReferenceNumber }) });
    }

    [HttpPost("{id:guid}/costs")]
    [Authorize(Policy = SystemPolicies.ManageBranch)]
    public async Task<ActionResult> AddCost(Guid id, SaveAssetCostRequest request, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken); if (asset is null) return NotFound();
        var scope = await staffScope.GetAsync(User); if (scope is null || !scope.HasAssetAccess(asset.BranchId, asset.DivisionId)) return Forbid();
        if (request.BookingId.HasValue && !await db.Bookings.AnyAsync(x => x.Id == request.BookingId && x.Items.Any(i => i.AssetId == id), cancellationToken))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["bookingId"] = ["The selected booking does not belong to this asset."] }));
        var entry = new AssetCostEntry { AssetId = id, BranchId = asset.BranchId, BookingId = request.BookingId, Category = request.Category, Description = request.Description.Trim(), Amount = request.Amount, OccurredOn = request.OccurredOn, Supplier = Normalize(request.Supplier), ReferenceNumber = Normalize(request.ReferenceNumber), RecordedByUserId = scope.UserId, RecordedByName = scope.UserName };
        db.AssetCostEntries.Add(entry); AuditWriter.Record(db, scope, "Asset cost recorded", nameof(AssetCostEntry), entry.Id, $"{entry.Category} cost of FJD {entry.Amount:N2} recorded against {asset.AssetNumber}.", asset.BranchId);
        await db.SaveChangesAsync(cancellationToken); return Ok(entry);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> ParsePhotoUrls(string? json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }

    private static bool HasValidImageSignature(string extension, byte[] bytes) => extension switch
    {
        ".png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        ".webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        _ => false,
    };

    private async Task<bool> ValidClassification(SaveAssetRequest request, CancellationToken token)
    {
        if (!request.DivisionId.HasValue) return false;
        if (request.ServiceOfferingId.HasValue && !await db.BranchDivisionServices.AnyAsync(x => x.BranchId == request.BranchId && x.DivisionId == request.DivisionId && x.ServiceOfferingId == request.ServiceOfferingId && x.IsActive, token)) return false;
        if (request.AssetCategoryId.HasValue && !await db.AssetCategories.AnyAsync(x => x.Id == request.AssetCategoryId && x.DivisionId == request.DivisionId && x.IsActive && (!request.ServiceOfferingId.HasValue || x.ServiceOfferingId == request.ServiceOfferingId), token)) return false;
        return true;
    }

    private static AssetResponse ToResponse(Asset asset, string branchName) => new(
        asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status,
        asset.DivisionId, asset.Division?.Name,
        asset.BranchId, branchName, asset.RegistrationNumber, asset.SerialNumber,
        asset.Category, asset.Manufacturer, asset.Model, asset.ModelYear, asset.VinOrChassisNumber, asset.EngineNumber, asset.MeterUnit, asset.CurrentMeterReading,
        asset.AcquisitionDate, asset.AcquisitionCost, asset.CurrentBookValue, asset.OwnershipType, asset.InsurancePolicyNumber, asset.InsuranceExpiry, asset.WarrantyExpiry,
        asset.DailyRate, asset.DefaultBondAmount, asset.NextServiceDate, asset.IsActive, asset.ServiceOfferingId, asset.AssetCategoryId, asset.CurrentLocation, asset.PhotoUrlsJson);
}

public sealed record SaveAssetRequest(
    [Required, MaxLength(50)] string AssetNumber,
    [Required, MaxLength(150)] string Name,
    AssetType Type,
    AssetStatus Status,
    Guid? DivisionId,
    Guid BranchId,
    [MaxLength(50)] string? RegistrationNumber,
    [MaxLength(100)] string? SerialNumber,
    [MaxLength(100)] string? Category,
    [MaxLength(100)] string? Manufacturer,
    [MaxLength(100)] string? Model,
    [Range(1900, 2200)] int? ModelYear,
    [MaxLength(100)] string? VinOrChassisNumber,
    [MaxLength(100)] string? EngineNumber,
    [MaxLength(30)] string? MeterUnit,
    [Range(0, 100000000)] decimal? CurrentMeterReading,
    DateOnly? AcquisitionDate,
    [Range(0, 100000000)] decimal AcquisitionCost,
    [Range(0, 100000000)] decimal? CurrentBookValue,
    [MaxLength(50)] string? OwnershipType,
    [MaxLength(100)] string? InsurancePolicyNumber,
    DateOnly? InsuranceExpiry,
    DateOnly? WarrantyExpiry,
    [Range(0, 1_000_000)] decimal DailyRate,
    [Range(0, 1_000_000)] decimal DefaultBondAmount,
    DateOnly? NextServiceDate,
    Guid? ServiceOfferingId = null,
    Guid? AssetCategoryId = null,
    string? CurrentLocation = null,
    string? PhotoUrlsJson = "[]");

public sealed record SetAssetStatusRequest(AssetStatus Status, bool IsActive);
public sealed record AssetResponse(
    Guid Id, string AssetNumber, string Name, AssetType Type, AssetStatus Status,
    Guid? DivisionId, string? DivisionName,
    Guid BranchId, string BranchName, string? RegistrationNumber, string? SerialNumber,
    string? Category, string? Manufacturer, string? Model, int? ModelYear, string? VinOrChassisNumber, string? EngineNumber, string? MeterUnit, decimal? CurrentMeterReading,
    DateOnly? AcquisitionDate, decimal AcquisitionCost, decimal? CurrentBookValue, string? OwnershipType, string? InsurancePolicyNumber, DateOnly? InsuranceExpiry, DateOnly? WarrantyExpiry,
    decimal DailyRate, decimal DefaultBondAmount, DateOnly? NextServiceDate, bool IsActive, Guid? ServiceOfferingId, Guid? AssetCategoryId, string? CurrentLocation, string PhotoUrlsJson);
public sealed record SaveAssetCostRequest(Guid? BookingId, AssetCostCategory Category, [Required, MaxLength(300)] string Description, [Range(0.01, 100000000)] decimal Amount, DateOnly OccurredOn, string? Supplier, string? ReferenceNumber);
