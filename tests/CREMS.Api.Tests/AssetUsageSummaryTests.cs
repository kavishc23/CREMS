using CREMS.Api.Domain.Assets;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class AssetUsageSummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
    private static Asset Asset() => new() { AssetNumber = "TEST-USAGE", Name = "Vehicle", CreatedAt = Now.AddYears(-1) };
    private static BookingItem Hire(Asset asset, int startHours, int endHours, bool actual = true,
        BookingStatus status = BookingStatus.Completed)
    {
        var booking = new Booking { BookingNumber = Guid.NewGuid().ToString(), Status = status };
        if (actual)
        {
            booking.Inspections.Add(new RentalInspection { Type = InspectionType.Handover, CompletedAt = Now.AddHours(startHours) });
            booking.Inspections.Add(new RentalInspection { Type = InspectionType.Return, CompletedAt = Now.AddHours(endHours) });
        }
        return new() { AssetId = asset.Id, BookingId = booking.Id, Booking = booking,
            StartAt = Now.AddHours(startHours - 2), EndAt = Now.AddHours(endHours + 2) };
    }
    private static MaintenanceJob Job(Asset asset, int start, int? release) => new() {
        AssetId = asset.Id, JobNumber = Guid.NewGuid().ToString(), ServiceType = "Repair",
        ReportedAt = Now.AddHours(start), CompletedAt = Now.AddHours((release ?? 0) - 2),
        ReleasedAt = release.HasValue ? Now.AddHours(release.Value) : null, Status = MaintenanceStatus.Completed };

    [Fact]
    public void Uses_actual_handover_and_return_and_includes_wait_for_safety_release()
    {
        var asset = Asset();
        var result = AssetUsageSummary.Calculate(asset, [Hire(asset, -20, -12)], [Job(asset, -10, -4)], [], [], 1, Now);
        Assert.Equal(24, result.TotalHours); Assert.Equal(8, result.RentedHours);
        Assert.Equal(6, result.MaintenanceHours); Assert.Equal(10, result.AvailableHours);
        Assert.Equal(18, result.InServiceHours); Assert.Equal(1, result.RentalCount);
        Assert.Equal(0, result.EstimatedRentalCount);
    }

    [Fact]
    public void Merges_overlaps_and_counts_conflicting_time_as_maintenance_once()
    {
        var asset = Asset();
        var result = AssetUsageSummary.Calculate(asset, [Hire(asset, -30, -8), Hire(asset, -14, -6)],
            [Job(asset, -12, -4), Job(asset, -10, -2)], [], [], 1, Now);
        Assert.Equal(12, result.RentedHours); Assert.Equal(10, result.MaintenanceHours);
        Assert.Equal(2, result.AvailableHours); Assert.Equal(6, result.ConflictingHours);
        Assert.Equal(result.TotalHours, result.Breakdown.Sum(x => x.Hours));
    }

    [Fact]
    public void Active_overdue_hire_continues_to_now_and_future_cancelled_and_draft_hires_do_not_count()
    {
        var asset = Asset(); var active = Hire(asset, -36, -30, false, BookingStatus.ConvertedToRental);
        active.Booking!.Inspections.Add(new RentalInspection { Type = InspectionType.Handover, CompletedAt = Now.AddHours(-36) });
        var result = AssetUsageSummary.Calculate(asset, [active, Hire(asset, 3, 12),
            Hire(asset, -18, -2, false, BookingStatus.Cancelled), Hire(asset, -18, -2, false, BookingStatus.Draft)], [], [], [], 1, Now);
        Assert.Equal(24, result.RentedHours); Assert.Equal(0, result.AvailableHours); Assert.Equal(1, result.RentalCount);
    }

    [Fact]
    public void Completed_legacy_hires_are_estimates_and_confirmed_reservations_are_separate()
    {
        var asset = Asset(); var old = Hire(asset, -20, -16, false);
        var reserved = Hire(asset, -8, -4, false, BookingStatus.Confirmed);
        var result = AssetUsageSummary.Calculate(asset, [old, reserved], [], [], [], 1, Now);
        Assert.Equal(8, result.RentedHours); Assert.Equal(8, result.ReservedHours);
        Assert.Equal(8, result.AvailableHours); Assert.Equal(1, result.EstimatedRentalCount);
    }

    [Fact]
    public void Maintenance_without_release_remains_down_and_cancelled_unstarted_job_is_excluded()
    {
        var asset = Asset(); var cancelled = Job(asset, -24, null); cancelled.Status = MaintenanceStatus.Cancelled;
        var result = AssetUsageSummary.Calculate(asset, [], [Job(asset, -10, null), cancelled], [], [], 1, Now);
        Assert.Equal(10, result.MaintenanceHours); Assert.Equal(14, result.AvailableHours); Assert.Equal(1, result.MaintenanceCount);
    }

    [Fact]
    public void Inspection_holds_and_recent_asset_creation_reduce_available_time()
    {
        var asset = Asset(); asset.CreatedAt = Now.AddHours(-12);
        var history = new[] { new AssetLifecycleEvent { AssetId = asset.Id, OccurredAt = Now.AddHours(-8),
            Type = AssetLifecycleEventType.MaintenanceWorkClosed, ToStatus = AssetStatus.Inspection, RecordedByName = "Test" },
            new AssetLifecycleEvent { AssetId = asset.Id, OccurredAt = Now.AddHours(-2),
            Type = AssetLifecycleEventType.ReturnedToService, ToStatus = AssetStatus.Available, RecordedByName = "Test" } };
        var result = AssetUsageSummary.Calculate(asset, [], [], history, [], 90, Now);
        Assert.Equal(12, result.TotalHours); Assert.Equal(6, result.OtherUnavailableHours); Assert.Equal(6, result.AvailableHours);
    }

    [Fact]
    public async Task Loads_all_history_instead_of_only_the_latest_fifty_records()
    {
        await using var db = MaintenanceJobsTests.Database(); var asset = Asset(); db.Assets.Add(asset);
        for (var i = 0; i < 60; i++) db.BookingItems.Add(Hire(asset, -200 + i * 3, -199 + i * 3));
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await AssetUsageSummary.Build(db, asset, 30, Now, TestContext.Current.CancellationToken);
        Assert.Equal(60, result.RentalCount); Assert.Equal(60, result.RentedHours);
        Assert.Equal(0, result.EstimatedRentalCount);
    }

    [Fact]
    public async Task Profile_usage_is_available_without_financial_permission_and_still_respects_asset_scope()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var actor = await db.Users.SingleAsync(TestContext.Current.CancellationToken);
        db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = actor.Id,
            Permission = SystemPermissions.AssetsViewFinancials, IsGranted = false });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new AssetProfilesController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        var response = Assert.IsType<OkObjectResult>(await controller.Profile(asset.Id, TestContext.Current.CancellationToken, 30));
        var json = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(30, json.GetProperty("usage").GetProperty("requestedDays").GetInt32());
        Assert.IsType<BadRequestObjectResult>(await controller.Profile(asset.Id, TestContext.Current.CancellationToken, 0));

        var (restricted, _) = await MaintenanceJobsTests.Setup(db, administrator: false);
        controller = new AssetProfilesController(db, new CurrentStaffScope(db)) { ControllerContext = restricted.ControllerContext };
        Assert.IsType<ForbidResult>(await controller.Profile(asset.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Calendar_includes_stable_category_ids_and_names_for_filtering()
    {
        await using var db = MaintenanceJobsTests.Database(); var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var category = new AssetCategory { Code = "SEDAN", Name = "Sedan" };
        asset.AssetCategory = category; asset.AssetCategoryId = category.Id;
        db.AssetCategories.Add(category);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new PlanningController(db, new CurrentStaffScope(db), null!) { ControllerContext = staff.ControllerContext };
        var response = Assert.IsType<OkObjectResult>(await controller.Calendar(Now, Now.AddDays(14), TestContext.Current.CancellationToken));
        var json = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var row = Assert.Single(json.GetProperty("assets").EnumerateArray());
        Assert.Equal(category.Id, row.GetProperty("assetCategoryId").GetGuid());
        Assert.Equal("Sedan", row.GetProperty("categoryName").GetString());
    }
}
