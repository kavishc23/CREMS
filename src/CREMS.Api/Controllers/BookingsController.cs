using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize(Policy = SystemPolicies.ManageRentals)]
public sealed class BookingsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookingResponse>>> GetAll(
        [FromQuery] BookingStatus? status,
        CancellationToken cancellationToken)
    {
        var query = db.Bookings.AsNoTracking();
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
        booking.Items.Select(item => new BookingItemResponse(
            item.Id, item.AssetId, item.Asset?.AssetNumber ?? string.Empty,
            item.Asset?.Name ?? string.Empty, item.StartAt, item.EndAt, item.DailyRate)).ToList());
}

public sealed record SetBookingStatusRequest(BookingStatus Status, string? Note);
public sealed record BookingItemResponse(
    Guid Id, Guid AssetId, string AssetNumber, string AssetName,
    DateTimeOffset StartAt, DateTimeOffset EndAt, decimal DailyRate);
public sealed record BookingResponse(
    Guid Id, string BookingNumber, BookingStatus Status, DateTimeOffset CreatedAt, string? Notes,
    Guid CustomerId, string CustomerName, string? CustomerEmail, string? CustomerPhone,
    bool CustomerIsBlocked, Guid BranchId, string BranchName,
    IReadOnlyCollection<BookingItemResponse> Items);
