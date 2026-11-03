using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class AssetAvailabilityListTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_marks_raw_available_asset_with_outstanding_return_as_rented()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        asset.Status = AssetStatus.Available;
        var booking = new Booking
        {
            BookingNumber = "OVERDUE-HIRE",
            BranchId = asset.BranchId,
            Status = BookingStatus.ConvertedToRental
        };
        db.Add(new BookingItem
        {
            Booking = booking,
            BookingId = booking.Id,
            AssetId = asset.Id,
            StartAt = DateTimeOffset.UtcNow.AddDays(-2),
            EndAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);

        var controller = new AssetsController(db, new CurrentStaffScope(db), null!)
        {
            ControllerContext = staff.ControllerContext
        };
        var response = await controller.GetAll(null, null, null, null, null, cancellationToken: Token);
        var rows = Assert.IsType<OkObjectResult>(response.Result).Value;
        var row = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AssetResponse>>(rows));

        Assert.Equal(AssetStatus.Available, row.Status);
        Assert.Equal(AssetStatus.Rented, row.OperationalStatus);
        Assert.Equal("Outstanding rental return", row.UnavailableReason);
    }

    [Fact]
    public async Task Available_filter_excludes_operationally_blocked_asset()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        asset.Status = AssetStatus.Available;
        var booking = new Booking { BookingNumber = "ACTIVE-HIRE", BranchId = asset.BranchId, Status = BookingStatus.ConvertedToRental };
        db.Add(new BookingItem { Booking = booking, BookingId = booking.Id, AssetId = asset.Id,
            StartAt = DateTimeOffset.UtcNow.AddHours(-2), EndAt = DateTimeOffset.UtcNow.AddHours(-1) });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var controller = new AssetsController(db, new CurrentStaffScope(db), null!) { ControllerContext = staff.ControllerContext };

        var response = await controller.GetAll(null, null, null, null, AssetStatus.Available, cancellationToken: Token);
        var rows = Assert.IsType<OkObjectResult>(response.Result).Value;

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<AssetResponse>>(rows));
    }
}
