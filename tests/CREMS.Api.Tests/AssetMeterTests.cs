using CREMS.Api.Domain.Identity;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public class AssetMeterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(MeterType.Odometer, "km")]
    [InlineData(MeterType.EngineHours, "hours")]
    public async Task Increasing_reading_stores_fuel_time_and_actor(MeterType type, string unit)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        asset.CurrentMeterReading = 100; asset.MeterUnit = unit;
        await db.SaveChangesAsync(Token);
        var controller = new AssetProfilesController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext };
        var before = DateTimeOffset.UtcNow;
        Assert.IsType<OkObjectResult>(await controller.Meter(asset.Id, new(type, unit, 120.5m, 75, MeterReadingSource.Manual, null), Token));
        var saved = await db.AssetMeterReadings.SingleAsync(Token);
        Assert.Equal(120.5m, saved.Reading);
        Assert.Equal(75m, saved.FuelPercent);
        Assert.Equal((await db.Users.SingleAsync(Token)).Id, saved.RecordedByUserId);
        Assert.InRange(saved.RecordedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(120.5m, asset.CurrentMeterReading);
        Assert.Equal(type, saved.Type);
        Assert.Contains(await db.AuditEvents.ToListAsync(Token), e => e.Action == "Asset meter recorded");
    }

    [Theory]
    [InlineData(null, -1, 50)]
    [InlineData(100, 99, 50)]
    [InlineData(100, 10000001, 50)]
    [InlineData(100, 120, -1)]
    [InlineData(100, 120, 101)]
    public async Task Invalid_values_leave_asset_and_history_unchanged(int? previous, int reading, int fuel)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        asset.CurrentMeterReading = previous; asset.MeterUnit = "km";
        await db.SaveChangesAsync(Token);
        var controller = new AssetProfilesController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext };
        Assert.IsType<BadRequestObjectResult>(await controller.Meter(asset.Id, new(MeterType.Odometer, "km", reading, fuel, MeterReadingSource.Manual, null), Token));
        Assert.Equal((decimal?)previous, asset.CurrentMeterReading);
        Assert.Empty(await db.AssetMeterReadings.ToListAsync(Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task Equal_reading_and_fuel_boundaries_are_valid(int fuel)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        asset.CurrentMeterReading = 100; asset.MeterUnit = "km";
        await db.SaveChangesAsync(Token);
        var controller = new AssetProfilesController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext };
        Assert.IsType<OkObjectResult>(await controller.Meter(asset.Id, new(MeterType.Odometer, "km", 100, fuel, MeterReadingSource.Manual, null), Token));
    }
    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    [InlineData(120)]
    public async Task Asset_edit_cannot_bypass_meter_history(int? reading)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        asset.CurrentMeterReading = 100;
        await db.SaveChangesAsync(Token);
        var request = System.Text.Json.JsonSerializer.Deserialize<SaveAssetRequest>("{}")! with
        { BranchId = asset.BranchId, DivisionId = asset.DivisionId, CurrentMeterReading = reading };
        var controller = new AssetsController(db, new CurrentStaffScope(db), null!) { ControllerContext = jobs.ControllerContext };
        Assert.IsType<BadRequestObjectResult>((await controller.Update(asset.Id, request, Token)).Result);
        Assert.Equal(100m, asset.CurrentMeterReading);
        Assert.Empty(await db.AssetMeterReadings.ToListAsync(Token));
    }

}
