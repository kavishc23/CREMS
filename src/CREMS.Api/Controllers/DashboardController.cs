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
public sealed class DashboardController(ApplicationDbContext db, CurrentStaffScope staffScope) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryResponse>> GetSummary(CancellationToken cancellationToken)
    {
        var scope = await staffScope.GetAsync(User);
        if (scope is null || (!scope.IsAdministrator && !scope.BranchId.HasValue)) return Forbid();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var assetQuery = db.Assets.AsNoTracking().Where(asset => asset.IsActive);
        var bookingQuery = db.Bookings.AsNoTracking();
        if (!scope.IsAdministrator)
        {
            assetQuery = assetQuery.Where(asset => asset.BranchId == scope.BranchId);
            bookingQuery = bookingQuery.Where(booking => booking.BranchId == scope.BranchId);
        }
        var assets = await assetQuery
            .Select(asset => new { asset.Type, asset.Status, asset.NextServiceDate })
            .ToListAsync(cancellationToken);

        var bookingCounts = await bookingQuery
            .GroupBy(booking => booking.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
        var overdueRentals = await bookingQuery
            .CountAsync(booking => booking.Status == BookingStatus.ConvertedToRental &&
                booking.Items.Any(item => item.EndAt < now), cancellationToken);
        var upcomingBookings = await bookingQuery
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
