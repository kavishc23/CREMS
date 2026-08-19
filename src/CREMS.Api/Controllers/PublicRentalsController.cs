using System.ComponentModel.DataAnnotations;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/public")]
public sealed class PublicRentalsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager) : ControllerBase
{
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
            })
            .ToListAsync(cancellationToken);

        return Ok(assets.Select(asset => new PublicAssetResponse(
            asset.Id, asset.AssetNumber, asset.Name, asset.Type, asset.Status,
            asset.BranchId, asset.BranchName, asset.DailyRate,
            asset.RegistrationNumber, asset.SerialNumber,
            asset.DivisionId, asset.DivisionName, asset.Category, asset.PersonnelRequirement,
            startDate.HasValue
                ? !unavailableAssetIds.Contains(asset.Id)
                : asset.Status == AssetStatus.Available)).ToList());
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

        var asset = await db.Assets.Include(item => item.Branch)
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

        var booking = new Booking
        {
            BookingNumber = $"REQ-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
            Customer = activeCustomer,
            BranchId = asset.BranchId,
            Status = BookingStatus.Draft,
            Notes = BuildRequestNotes(request),
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
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new PublicBookingResponse(
            booking.BookingNumber, asset.Name, asset.Branch.Name,
            request.StartDate, request.EndDate, "Request received"));
    }

    private static string BuildRequestNotes(PublicBookingRequest request)
    {
        var details = new List<string>
        {
            "Submitted through the public rental website.",
            $"Contact person: {request.FullName.Trim()}",
        };
        if (!string.IsNullOrWhiteSpace(request.Purpose))
            details.Add($"Rental purpose: {request.Purpose.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Message))
            details.Add($"Customer message: {request.Message.Trim()}");
        return string.Join("\n", details);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
    string? DivisionName, string? Category, PersonnelRequirement PersonnelRequirement, bool IsAvailable);
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
    [MaxLength(1000)] string? Message);
public sealed record PublicBookingResponse(
    string Reference, string AssetName, string BranchName,
    DateOnly StartDate, DateOnly EndDate, string Status);
public sealed record PublicBookingStatusResponse(
    string Reference, string Status, string Message, string? AssetName,
    string BranchName, DateTimeOffset? StartAt, DateTimeOffset? EndAt,
    DateTimeOffset SubmittedAt);
