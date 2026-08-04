using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = SystemPolicies.StaffPortal)]
public sealed class DashboardController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryResponse>> GetSummary(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var assets = await db.Assets.AsNoTracking().Where(asset => asset.IsActive)
            .Select(asset => new { asset.Type, asset.Status, asset.NextServiceDate })
            .ToListAsync(cancellationToken);

        var bookingCounts = await db.Bookings.AsNoTracking()
            .GroupBy(booking => booking.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var overdueRentals = await db.Bookings.AsNoTracking()
            .CountAsync(booking => booking.Status == BookingStatus.ConvertedToRental &&
                booking.Items.Any(item => item.EndAt < now), cancellationToken);
        var upcomingBookings = await db.Bookings.AsNoTracking()
            .CountAsync(booking => booking.Status == BookingStatus.Confirmed &&
                booking.Items.Any(item => item.StartAt >= now), cancellationToken);

        var vehicleTotal = assets.Count(asset => asset.Type == AssetType.Vehicle);
        var equipmentTotal = assets.Count(asset => asset.Type == AssetType.Equipment);
        var vehicleRented = assets.Count(asset => asset.Type == AssetType.Vehicle && asset.Status == AssetStatus.Rented);
        var equipmentRented = assets.Count(asset => asset.Type == AssetType.Equipment && asset.Status == AssetStatus.Rented);

        return Ok(new DashboardSummaryResponse(
            assets.Count(asset => asset.Status == AssetStatus.Available),
            bookingCounts.GetValueOrDefault(BookingStatus.ConvertedToRental),
            assets.Count(asset => asset.Status == AssetStatus.Maintenance),
            overdueRentals,
            bookingCounts.GetValueOrDefault(BookingStatus.Draft),
            upcomingBookings,
            assets.Count(asset => asset.NextServiceDate.HasValue &&
                asset.NextServiceDate.Value >= today && asset.NextServiceDate.Value <= today.AddDays(30)),
            vehicleTotal == 0 ? 0 : (int)Math.Round(vehicleRented * 100d / vehicleTotal),
            equipmentTotal == 0 ? 0 : (int)Math.Round(equipmentRented * 100d / equipmentTotal),
            assets.Count));
    }
}

public sealed record DashboardSummaryResponse(
    int AvailableAssets, int ActiveRentals, int UnderMaintenance, int OverdueRentals,
    int PendingRequests, int UpcomingBookings, int ServicesDueSoon,
    int VehicleUtilization, int EquipmentUtilization, int TotalAssets);
