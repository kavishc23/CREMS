using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public class AssetTransferTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static async Task<(CorporateOperationsController controller, Asset asset, Branch destination)> Setup(ApplicationDbContext db)
    {
        var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        var destination = new Branch { Code = "DEST", Name = "Destination" };
        asset.CurrentLocation = "Source"; asset.CurrentMeterReading = 100;
        db.Branches.Add(destination);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        return (new CorporateOperationsController(db, new CurrentStaffScope(db), new EmailQueue(), null!) { ControllerContext = jobs.ControllerContext }, asset, destination);
    }
    private static async Task<AssetTransfer> Create(CorporateOperationsController controller, Asset asset, Branch destination)
        => Assert.IsType<AssetTransfer>(Assert.IsType<OkObjectResult>(await controller.CreateTransfer(new(asset.Id, asset.BranchId, destination.Id, "Redeployment", 0), Token)).Value);

    [Fact]
    public async Task Progress_blocks_hire_moves_location_and_preserves_history()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, asset, destination) = await Setup(db);
        var source = asset.BranchId;
        var transfer = await Create(controller, asset, destination);
        Assert.Equal(AssetStatus.Available, asset.Status);
        foreach (var status in new[] { TransferStatus.Approved, TransferStatus.InTransit, TransferStatus.Received, TransferStatus.Inspected })
        {
            Assert.IsType<OkObjectResult>(await controller.SetTransferStatus(transfer.Id, new(status, 110, status.ToString(), null, ["test-evidence"]), Token));
            Assert.Equal(status == TransferStatus.Inspected ? destination.Id : source, asset.BranchId);
            Assert.Equal(status == TransferStatus.Inspected, BookingPolicy.IsOperational(asset));
            if (status == TransferStatus.InTransit) Assert.Contains("In transit", asset.CurrentLocation);
            if (status is TransferStatus.Received or TransferStatus.Inspected) Assert.Equal(destination.Name, asset.CurrentLocation);
        }
        Assert.NotNull(transfer.ApprovedByUserId); Assert.NotNull(transfer.DepartedAt); Assert.NotNull(transfer.ReceivedAt);
        var history = await db.AssetLifecycleEvents.ToListAsync(Token);
        Assert.Equal(4, history.Count);
        Assert.All(history, e => { Assert.NotEqual(Guid.Empty, e.RecordedByUserId); Assert.Contains("test-evidence", e.EvidenceJson); Assert.Contains(transfer.TransferNumber, e.Notes); });
        Assert.Equal(110m, asset.CurrentMeterReading);
        Assert.IsType<BadRequestObjectResult>(await controller.SetTransferStatus(transfer.Id, new(TransferStatus.Cancelled, null, null, null, null), Token));
    }

    [Theory]
    [InlineData(TransferStatus.Approved, true)]
    [InlineData(TransferStatus.InTransit, false)]
    [InlineData(TransferStatus.Received, false)]
    public async Task Cancellation_only_before_departure(TransferStatus stage, bool allowed)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, asset, destination) = await Setup(db); var transfer = await Create(controller, asset, destination);
        foreach (var status in new[] { TransferStatus.Approved, TransferStatus.InTransit, TransferStatus.Received }.TakeWhile(s => s <= stage))
            Assert.IsType<OkObjectResult>(await controller.SetTransferStatus(transfer.Id, new(status, 100, "Good", null, null), Token));
        var result = await controller.SetTransferStatus(transfer.Id, new(TransferStatus.Cancelled, null, null, null, null), Token);
        if (allowed) { Assert.IsType<OkObjectResult>(result); Assert.Equal(AssetStatus.Available, asset.Status); }
        else { Assert.IsType<BadRequestObjectResult>(result); Assert.Equal(stage, transfer.Status); }
    }

    [Fact]
    public async Task Duplicate_transfer_and_direct_release_are_blocked()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, asset, destination) = await Setup(db); var transfer = await Create(controller, asset, destination);
        Assert.IsType<BadRequestObjectResult>(await controller.CreateTransfer(new(asset.Id, asset.BranchId, destination.Id, "Duplicate", 0), Token));
        Assert.IsType<OkObjectResult>(await controller.SetTransferStatus(transfer.Id, new(TransferStatus.Approved, null, null, null, null), Token));
        asset.Status = AssetStatus.Available;
        await Assert.ThrowsAsync<MaintenanceConflictException>(() => db.SaveChangesAsync(Token));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(109)]
    [InlineData(10000001)]
    public async Task Invalid_arrival_reading_is_rejected(int reading)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, asset, destination) = await Setup(db); var transfer = await Create(controller, asset, destination);
        await controller.SetTransferStatus(transfer.Id, new(TransferStatus.Approved, null, null, null, null), Token);
        await controller.SetTransferStatus(transfer.Id, new(TransferStatus.InTransit, 110, "Departure", null, null), Token);
        Assert.IsType<BadRequestObjectResult>(await controller.SetTransferStatus(transfer.Id, new(TransferStatus.Received, reading, "Arrival", null, null), Token));
        Assert.Equal(TransferStatus.InTransit, transfer.Status); Assert.Equal(110m, asset.CurrentMeterReading);
    }

    [Fact]
    public async Task Destination_must_support_division_and_be_different_and_active()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, asset, destination) = await Setup(db);
        Assert.IsType<BadRequestObjectResult>(await controller.CreateTransfer(new(asset.Id, asset.BranchId, asset.BranchId, "Same", 0), Token));
        asset.DivisionId = Guid.NewGuid(); await db.SaveChangesAsync(Token);
        Assert.IsType<BadRequestObjectResult>(await controller.CreateTransfer(new(asset.Id, asset.BranchId, destination.Id, "Wrong division", 0), Token));
        db.BranchDivisions.Add(new() { BranchId = destination.Id, DivisionId = asset.DivisionId.Value, IsActive = true });
        destination.IsActive = false; await db.SaveChangesAsync(Token);
        Assert.IsType<BadRequestObjectResult>(await controller.CreateTransfer(new(asset.Id, asset.BranchId, destination.Id, "Inactive", 0), Token));
    }

    [Fact]
    public async Task Booking_added_after_request_blocks_approval()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, asset, destination) = await Setup(db); var transfer = await Create(controller, asset, destination);
        var booking = new Booking { BookingNumber = "BOOK", Status = BookingStatus.Confirmed, BranchId = asset.BranchId };
        db.Bookings.Add(booking); db.BookingItems.Add(new() { BookingId = booking.Id, AssetId = asset.Id, StartAt = DateTimeOffset.UtcNow.AddDays(1), EndAt = DateTimeOffset.UtcNow.AddDays(2) });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        Assert.IsType<BadRequestObjectResult>(await controller.SetTransferStatus(transfer.Id, new(TransferStatus.Approved, null, null, null, null), Token));
        Assert.Equal(TransferStatus.Requested, transfer.Status); Assert.Equal(AssetStatus.Available, asset.Status);
    }
    [Fact]
    public async Task New_booking_cannot_allocate_an_approved_transfer_asset()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (controller, asset, destination) = await Setup(db); var transfer = await Create(controller, asset, destination);
        await controller.SetTransferStatus(transfer.Id, new(TransferStatus.Approved, null, null, null, null), Token);
        db.BookingItems.Add(new() { BookingId = Guid.NewGuid(), AssetId = asset.Id });
        await Assert.ThrowsAsync<MaintenanceConflictException>(() => db.SaveChangesAsync(Token));
    }

}
