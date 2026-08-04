using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class BookingsController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookingResponse>>> GetAll(
        [FromQuery] BookingStatus? status,
        CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var query = db.Bookings.AsNoTracking();
        if (!scope.IsAdministrator) query = query.Where(booking => booking.BranchId == scope.BranchId);
        if (status.HasValue) query = query.Where(booking => booking.Status == status);

        var bookings = await query
            .OrderByDescending(booking => booking.CreatedAt)
            .Select(booking => new BookingResponse(
                booking.Id,
                booking.BookingNumber,
                booking.Status,
                booking.CreatedAt,
                booking.Notes,
                booking.CustomerId,
                booking.Customer!.Name,
                booking.Customer.Email,
                booking.Customer.Phone,
                booking.Customer.IsBlocked,
                booking.BranchId,
                booking.Branch!.Name,
                booking.DiscountAmount,
                booking.TaxRate,
                booking.DepositRequired,
                booking.AdditionalCharges,
                booking.AdditionalChargesDescription,
                booking.ApprovedAt,
                booking.Items.Select(item => new BookingItemResponse(
                    item.Id,
                    item.AssetId,
                    item.Asset!.AssetNumber,
                    item.Asset.Name,
                    item.StartAt,
                    item.EndAt,
                    item.DailyRate)).ToList()))
            .ToListAsync(cancellationToken);
        return Ok(bookings);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BookingResponse>> Update(
        Guid id,
        UpdateBookingRequest request,
        CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.Include(item => item.Customer).Include(item => item.Branch)
            .Include(item => item.Items).ThenInclude(item => item.Asset)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasBranchAccess(booking.BranchId) || !scope.HasBranchAccess(request.BranchId)) return Forbid();
        if (booking.Status is BookingStatus.ConvertedToRental or BookingStatus.Completed)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["status"] = ["Active or completed rentals cannot be reassigned."] }));

        var asset = await db.Assets.FirstOrDefaultAsync(item => item.Id == request.AssetId &&
            item.BranchId == request.BranchId && item.IsActive, cancellationToken);
        var customer = await db.Customers.FirstOrDefaultAsync(item => item.Id == request.CustomerId && item.IsActive, cancellationToken);
        var branch = await db.Branches.FirstOrDefaultAsync(item => item.Id == request.BranchId && item.IsActive, cancellationToken);
        if (asset is null || customer is null || branch is null || request.EndAt <= request.StartAt)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["booking"] = ["Select a valid branch, customer, asset and rental period."] }));

        var previous = $"Branch: {booking.BranchId}; Customer: {booking.CustomerId}; Asset: {booking.Items.FirstOrDefault()?.AssetId}";
        booking.BranchId = branch.Id; booking.Branch = branch; booking.CustomerId = customer.Id; booking.Customer = customer;
        booking.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        booking.DiscountAmount = request.DiscountAmount; booking.TaxRate = request.TaxRate;
        booking.DepositRequired = request.DepositRequired; booking.AdditionalCharges = request.AdditionalCharges;
        booking.AdditionalChargesDescription = string.IsNullOrWhiteSpace(request.AdditionalChargesDescription) ? null : request.AdditionalChargesDescription.Trim();
        var item = booking.Items.FirstOrDefault();
        if (item is null) { item = new BookingItem(); booking.Items.Add(item); }
        item.AssetId = asset.Id; item.Asset = asset; item.StartAt = request.StartAt; item.EndAt = request.EndAt; item.DailyRate = request.DailyRate;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        AuditWriter.Record(db, scope, "Booking updated", "Booking", booking.Id,
            $"{booking.BookingNumber} details and pricing were updated.", booking.BranchId, previous,
            $"Branch: {booking.BranchId}; Customer: {booking.CustomerId}; Asset: {asset.Id}");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(booking));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<BookingResponse>> SetStatus(
        Guid id,
        SetBookingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var booking = await db.Bookings
            .Include(item => item.Customer)
            .Include(item => item.Branch)
            .Include(item => item.Items).ThenInclude(item => item.Asset)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (booking is null) return NotFound();
        var scope = await staffScope.GetAsync(User);
        if (scope is null || !scope.HasBranchAccess(booking.BranchId)) return Forbid();

        if (!IsValidTransition(booking.Status, request.Status))
        {
            ModelState.AddModelError(nameof(request.Status),
                $"A {booking.Status} booking cannot be changed to {request.Status}.");
            return ValidationProblem(ModelState);
        }

        if (request.Status == BookingStatus.Confirmed)
        {
            if (booking.Customer?.IsBlocked == true || booking.Customer?.IsActive == false)
            {
                ModelState.AddModelError(nameof(booking.CustomerId),
                    "This customer is not currently eligible to rent.");
                return ValidationProblem(ModelState);
            }
            booking.ApprovedByUserId = scope.UserId;
            booking.ApprovedAt = DateTimeOffset.UtcNow;

            foreach (var item in booking.Items)
            {
                var conflict = await db.BookingItems.AsNoTracking().AnyAsync(other =>
                    other.BookingId != booking.Id &&
                    other.AssetId == item.AssetId &&
                    other.StartAt < item.EndAt && other.EndAt > item.StartAt &&
                    other.Booking!.Status == BookingStatus.Confirmed,
                    cancellationToken);
                if (conflict)
                {
                    ModelState.AddModelError(nameof(booking.Items),
                        $"{item.Asset?.Name ?? "An asset"} has another confirmed booking for these dates.");
                    return ValidationProblem(ModelState);
                }
            }
        }

        booking.Status = request.Status;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        if (request.Status == BookingStatus.ConvertedToRental)
        {
            foreach (var item in booking.Items.Where(item => item.Asset is not null))
                item.Asset!.Status = AssetStatus.Rented;
        }
        else if (request.Status == BookingStatus.Completed)
        {
            foreach (var item in booking.Items.Where(item => item.Asset is not null))
                item.Asset!.Status = AssetStatus.Available;
        }
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            var note = request.Note.Trim();
            booking.Notes = string.IsNullOrWhiteSpace(booking.Notes)
                ? note
                : $"{booking.Notes}\nStaff note: {note}";
        }

        AuditWriter.Record(db, scope, "Booking status changed", "Booking", booking.Id,
            $"{booking.BookingNumber} changed to {request.Status}.", booking.BranchId,
            null, request.Status.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(booking));
    }

    private static bool IsValidTransition(BookingStatus current, BookingStatus next) =>
        current == next || (current, next) switch
        {
            (BookingStatus.Draft, BookingStatus.Confirmed) => true,
            (BookingStatus.Draft, BookingStatus.Cancelled) => true,
            (BookingStatus.Confirmed, BookingStatus.Cancelled) => true,
            (BookingStatus.Confirmed, BookingStatus.ConvertedToRental) => true,
            (BookingStatus.ConvertedToRental, BookingStatus.Completed) => true,
            _ => false,
        };

    private static BookingResponse ToResponse(Booking booking) => new(
        booking.Id, booking.BookingNumber, booking.Status, booking.CreatedAt, booking.Notes,
        booking.CustomerId, booking.Customer?.Name ?? string.Empty,
        booking.Customer?.Email, booking.Customer?.Phone, booking.Customer?.IsBlocked ?? false,
        booking.BranchId, booking.Branch?.Name ?? string.Empty,
        booking.DiscountAmount, booking.TaxRate, booking.DepositRequired, booking.AdditionalCharges,
        booking.AdditionalChargesDescription, booking.ApprovedAt,
        booking.Items.Select(item => new BookingItemResponse(
            item.Id, item.AssetId, item.Asset?.AssetNumber ?? string.Empty,
            item.Asset?.Name ?? string.Empty, item.StartAt, item.EndAt, item.DailyRate)).ToList());
}

public sealed record SetBookingStatusRequest(BookingStatus Status, string? Note);
public sealed record UpdateBookingRequest(Guid BranchId, Guid CustomerId, Guid AssetId,
    DateTimeOffset StartAt, DateTimeOffset EndAt, decimal DailyRate, string? Notes,
    decimal DiscountAmount, decimal TaxRate, decimal DepositRequired, decimal AdditionalCharges,
    string? AdditionalChargesDescription);
public sealed record BookingItemResponse(
    Guid Id, Guid AssetId, string AssetNumber, string AssetName,
    DateTimeOffset StartAt, DateTimeOffset EndAt, decimal DailyRate);
public sealed record BookingResponse(
    Guid Id, string BookingNumber, BookingStatus Status, DateTimeOffset CreatedAt, string? Notes,
    Guid CustomerId, string CustomerName, string? CustomerEmail, string? CustomerPhone,
    bool CustomerIsBlocked, Guid BranchId, string BranchName,
    decimal DiscountAmount, decimal TaxRate, decimal DepositRequired, decimal AdditionalCharges,
    string? AdditionalChargesDescription, DateTimeOffset? ApprovedAt,
    IReadOnlyCollection<BookingItemResponse> Items);
