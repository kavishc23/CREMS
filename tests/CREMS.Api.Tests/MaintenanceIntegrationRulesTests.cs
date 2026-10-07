using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class MaintenanceIntegrationRulesTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("1.5")]
    [InlineData("100001")]
    public async Task Parts_reject_non_whole_or_invalid_quantities(string quantity)
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, job, part) = await Setup(db);
        Assert.IsType<BadRequestObjectResult>(await controller.UsePart(job.Id, new(part.Id, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture)), TestContext.Current.CancellationToken));
        Assert.Equal(5, part.QuantityOnHand); Assert.Empty(db.MaintenancePartUsages);
    }

    [Theory]
    [InlineData(MaintenanceStatus.Completed)]
    [InlineData(MaintenanceStatus.Cancelled)]
    public async Task Closed_jobs_cannot_consume_stock(MaintenanceStatus status)
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, job, part) = await Setup(db);
        job.Status = status; using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.IsType<BadRequestObjectResult>(await controller.UsePart(job.Id, new(part.Id, 1), TestContext.Current.CancellationToken));
        Assert.Equal(5, part.QuantityOnHand);
    }

    [Fact]
    public async Task Parts_are_scoped_to_job_division_and_stock_branch()
    {
        await using var db = MaintenanceJobsTests.Database(); var (_, job, part) = await Setup(db);
        var (staff, _) = await MaintenanceJobsTests.Setup(db, administrator: false);
        var staffId = Guid.Parse(staff.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var user = await db.Users.SingleAsync(x => x.Id == staffId, TestContext.Current.CancellationToken);
        user.BranchId = job.BranchId; job.Asset!.DivisionId = Guid.NewGuid();
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new BusinessOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        Assert.IsType<ForbidResult>(await controller.UsePart(job.Id, new(part.Id, 1), TestContext.Current.CancellationToken));
        job.Asset.DivisionId = user.DivisionId; part.BranchId = Guid.NewGuid();
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        controller = new BusinessOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        Assert.IsType<ForbidResult>(await controller.UsePart(job.Id, new(part.Id, 1), TestContext.Current.CancellationToken));
        Assert.Equal(5, part.QuantityOnHand); Assert.Empty(db.MaintenancePartUsages);
    }

    [Fact]
    public async Task Parts_respect_allocations_and_preserve_existing_invoice_cost()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, job, part) = await Setup(db);
        part.QuantityAllocated = 4; job.ActualCost = 45;
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.IsType<BadRequestObjectResult>(await controller.UsePart(job.Id, new(part.Id, 2), TestContext.Current.CancellationToken));
        Assert.IsType<OkObjectResult>(await controller.UsePart(job.Id, new(part.Id, 1), TestContext.Current.CancellationToken));
        Assert.Equal(4, part.QuantityOnHand); Assert.Equal(10m, job.PartsCost); Assert.Equal(45m, job.OtherCost); Assert.Equal(55m, job.ActualCost);
        Assert.NotNull(job.UpdatedAt); Assert.Single(db.MaintenancePartUsages);
    }

    [Fact]
    public async Task Asset_release_cannot_bypass_open_repairs_even_via_direct_asset_edits()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, job, _) = await Setup(db);
        await Assert.ThrowsAsync<MaintenanceConflictException>(() => controller.RecordLifecycle(job.AssetId,
            new(AssetLifecycleEventType.ReturnedToService, AssetStatus.Available, null, null, null, null), TestContext.Current.CancellationToken));
        // A failed save remains uncommitted; a new request must still see the unavailable asset.
        db.ChangeTracker.Clear();
        var asset = await db.Assets.SingleAsync(x => x.Id == job.AssetId, TestContext.Current.CancellationToken);
        Assert.Equal(AssetStatus.Maintenance, asset.Status);
        asset.Status = AssetStatus.Available;
        await Assert.ThrowsAsync<MaintenanceConflictException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Stale_job_form_cannot_overwrite_a_parts_issue()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, job, part) = await Setup(db);
        var version = job.UpdatedAt ?? job.CreatedAt;
        var (jobs, _) = await MaintenanceJobsTests.Setup(db);
        await controller.UsePart(job.Id, new(part.Id, 1), TestContext.Current.CancellationToken);
        Assert.IsType<ConflictObjectResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { ExpectedVersion = version }, TestContext.Current.CancellationToken));
        Assert.Equal(10m, job.PartsCost);
    }

    [Fact]
    public async Task Preventive_jobs_use_Fiji_date_meter_thresholds_and_do_not_duplicate_open_work()
    {
        await using var db = MaintenanceJobsTests.Database(); var (_, asset) = await MaintenanceJobsTests.Setup(db);
        var now = DateTimeOffset.Parse("2026-10-05T13:00:00Z");
        Assert.Equal(new DateOnly(2026, 10, 6), MaintenanceRules.LocalDate(now));
        asset.NextServiceDate = new DateOnly(2026, 10, 6);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await MaintenanceRules.GeneratePreventiveJobsAsync(db, now, TestContext.Current.CancellationToken));
        Assert.Equal(0, await MaintenanceRules.GeneratePreventiveJobsAsync(db, now, TestContext.Current.CancellationToken));
        Assert.Equal(AssetStatus.Maintenance, asset.Status); Assert.Single(db.AssetLifecycleEvents);
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        job.Status = MaintenanceStatus.Completed; job.CompletedAt = now; job.ReleasedAt = now; job.NextServiceMeter = 100;
        asset.NextServiceDate = null; asset.CurrentMeterReading = 100; asset.Status = AssetStatus.Available;
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Single(await MaintenanceRules.DueAssets(db, db.Assets, MaintenanceRules.LocalDate(now)).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await MaintenanceRules.GeneratePreventiveJobsAsync(db, now, TestContext.Current.CancellationToken));
        Assert.Contains(await db.MaintenanceJobs.ToListAsync(TestContext.Current.CancellationToken), x => x.Status == MaintenanceStatus.Open && x.FaultDescription.StartsWith("Meter-based"));
    }

    [Fact]
    public async Task Branch_changes_cannot_strand_an_open_repair()
    {
        await using var db = MaintenanceJobsTests.Database(); var (_, job, _) = await Setup(db);
        job.Asset!.BranchId = Guid.NewGuid();
        await Assert.ThrowsAsync<MaintenanceConflictException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        db.ChangeTracker.Clear();
        Assert.Equal(job.BranchId, (await db.Assets.SingleAsync(TestContext.Current.CancellationToken)).BranchId);
    }

    [Fact]
    public async Task Meter_scheduler_catches_thresholds_crossed_while_on_hire_without_duplicate_jobs()
    {
        await using var db = MaintenanceJobsTests.Database(); var (_, asset) = await MaintenanceJobsTests.Setup(db);
        var service = new CREMS.Api.Domain.Common.ServiceOffering { Code = "METER", Name = "Meter service", MaintenanceRulesJson = "{\"meterInterval\":1000}" };
        db.ServiceOfferings.Add(service); asset.ServiceOfferingId = service.Id;
        asset.CurrentMeterReading = 1001; asset.Status = AssetStatus.Rented;
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, await MaintenanceRules.GeneratePreventiveJobsAsync(db, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
        asset.Status = AssetStatus.Available;
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await MaintenanceRules.GeneratePreventiveJobsAsync(db, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
        Assert.Equal(0, await MaintenanceRules.GeneratePreventiveJobsAsync(db, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken));
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(job.IsPreventive); Assert.Equal(2001m, job.NextServiceMeter);
        Assert.Equal(AssetStatus.Maintenance, asset.Status);
    }

    [Fact]
    public async Task Corrective_completion_does_not_hide_an_existing_meter_schedule()
    {
        await using var db = MaintenanceJobsTests.Database(); var (_, asset) = await MaintenanceJobsTests.Setup(db);
        asset.CurrentMeterReading = 1000;
        db.MaintenanceJobs.AddRange(
            new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "PREVENTIVE", ServiceType = "Service", Status = MaintenanceStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow.AddDays(-1), NextServiceMeter = 1000 },
            new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "CORRECTIVE", ServiceType = "Repair", Status = MaintenanceStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Single(await MaintenanceRules.DueAssets(db, db.Assets, MaintenanceRules.LocalDate(DateTimeOffset.UtcNow)).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Transfer_can_proceed_after_repairs_close_and_historical_job_cannot_reopen_at_old_branch()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        var destination = new CREMS.Api.Domain.Common.Branch { Code = "DEST", Name = "Destination" };
        var transfer = new AssetTransfer { TransferNumber = "TEST-TRANSFER", AssetId = asset.Id, FromBranchId = asset.BranchId,
            ToBranchId = destination.Id, Reason = "Redeployment" };
        db.AddRange(destination, transfer);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        var operations = new CorporateOperationsController(db, new CurrentStaffScope(db), new EmailQueue(), null!) { ControllerContext = jobs.ControllerContext };
        Assert.IsType<BadRequestObjectResult>(await operations.SetTransferStatus(transfer.Id, new(TransferStatus.Approved, null, null, null, null), token));
        Assert.Equal(TransferStatus.Requested, transfer.Status);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed), token));
        var workspace = new MaintenanceWorkspaceController(db, new CurrentStaffScope(db), new MaintenanceJobsTests.MaintenanceAuthorization(db)) { ControllerContext = jobs.ControllerContext };
        Assert.IsType<OkObjectResult>(await workspace.Release(job.Id, new(job.UpdatedAt ?? job.CreatedAt, "Safety check passed before transfer", 100, MaintenanceWorkspace.Checks(asset, null)), token));
        foreach (var status in new[] { TransferStatus.Approved, TransferStatus.InTransit, TransferStatus.Received, TransferStatus.Inspected })
            Assert.IsType<OkObjectResult>(await operations.SetTransferStatus(transfer.Id, new(status, 100, "Good", null, null), token));
        Assert.Equal(destination.Id, asset.BranchId);
        Assert.IsType<BadRequestObjectResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress), token));
        Assert.Equal(MaintenanceStatus.Completed, job.Status);
    }

    private static async Task<(BusinessOperationsController, MaintenanceJob, InventoryPart)> Setup(ApplicationDbContext db)
    {
        var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), TestContext.Current.CancellationToken);
        var job = await db.MaintenanceJobs.Include(x => x.Asset).SingleAsync(TestContext.Current.CancellationToken);
        var part = new InventoryPart { PartNumber = "PART", Name = "Test part", BranchId = asset.BranchId, QuantityOnHand = 5, UnitCost = 10 };
        db.InventoryParts.Add(part); using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (new BusinessOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext }, job, part);
    }
}
