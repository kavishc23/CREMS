using CREMS.Api.Domain.Identity;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CREMS.Api.Tests;
public class MaintenanceStatusRegressionTests
{
    [Fact]
    public async Task Typed_technician_can_be_created_edited_and_cleared_without_personnel_record()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        Assert.IsType<OkObjectResult>(await jobs.Create(new(asset.Id, "Repair", "Fault", "  Workshop technician  ", null, 0, null), token));
        var job = await db.MaintenanceJobs.SingleAsync(token);
        Assert.Equal("Workshop technician", job.AssignedTo); Assert.Null(job.AssignedPersonnelId);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { AssignedTo = "New technician", AssignedPersonnelId = null }, token));
        Assert.Equal("New technician", job.AssignedTo); Assert.Null(job.AssignedPersonnelId);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { AssignedTo = "  ", AssignedPersonnelId = null }, token));
        Assert.Null(job.AssignedTo);
    }

    [Fact]
    public async Task Blocking_jobs_include_old_open_work_beyond_history_limit_and_exclude_closed_jobs()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        foreach (var status in new[] { MaintenanceStatus.Open, MaintenanceStatus.InProgress, MaintenanceStatus.WaitingForParts })
            db.Add(new MaintenanceJob { JobNumber = "BLOCK-" + status, AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = "Outstanding repair", Status = status, ReportedAt = DateTimeOffset.UtcNow.AddYears(-1) });
        for (var i = 0; i < 55; i++) db.Add(new MaintenanceJob { JobNumber = "CLOSED-" + i, AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = "Closed repair", Status = i % 2 == 0 ? MaintenanceStatus.Completed : MaintenanceStatus.Cancelled });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        var workspace = new MaintenanceWorkspaceController(db, new CurrentStaffScope(db), new MaintenanceJobsTests.MaintenanceAuthorization(db)) { ControllerContext = jobs.ControllerContext };
        var result = System.Text.Json.JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await workspace.Detail(job.Id, token)).Value);
        Assert.Equal(50, result.GetProperty("history").GetArrayLength());
        var blockers = result.GetProperty("blockingJobs").EnumerateArray().ToArray();
        Assert.Equal(3, blockers.Length);
        Assert.All(blockers, row => Assert.StartsWith("BLOCK-", row.GetProperty("jobNumber").GetString()));
        Assert.Equal(0, result.GetProperty("restrictedBlockingJobCount").GetInt32());
    }

    [Fact]
    public async Task Invoice_total_mode_should_ignore_previous_labour_hours()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { PartsCost = 0, LabourHours = 2, LabourRate = 30, UseDetailedCosts = true }, token));
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { PartsCost = 0, LabourCost = 0, LabourHours = 2, LabourRate = 30, UseDetailedCosts = false, ActualCost = 100 }, token));
        Assert.Equal(100m, job.ActualCost);
        Assert.Null(job.LabourHours); Assert.Null(job.LabourRate); Assert.Equal(0m, job.LabourCost);
        var reports = new BusinessOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext };
        var report = System.Text.Json.JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>((await reports.AssetProfitability(token)).Result).Value);
        Assert.Equal(100m, report.GetProperty("assets")[0].GetProperty("maintenanceCost").GetDecimal());
        Assert.Equal(100m, report.GetProperty("totals").GetProperty("expense").GetDecimal());
        Assert.Equal(-100m, report.GetProperty("totals").GetProperty("profit").GetDecimal());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_job_requires_reopening_before_cancellation(bool released)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed), token));
        var workspace = new MaintenanceWorkspaceController(db, new CurrentStaffScope(db), new MaintenanceJobsTests.MaintenanceAuthorization(db)) { ControllerContext = jobs.ControllerContext };
        if (released) Assert.IsType<OkObjectResult>(await workspace.Release(job.Id, new(job.UpdatedAt ?? job.CreatedAt, "Passed", 100, MaintenanceWorkspace.Checks(asset,null)), token));
        var completedAt = job.CompletedAt; var releasedAt = job.ReleasedAt;
        Assert.IsType<BadRequestObjectResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Cancelled), token));
        Assert.Equal(MaintenanceStatus.Completed, job.Status); Assert.Equal(completedAt, job.CompletedAt); Assert.Equal(releasedAt, job.ReleasedAt);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress), token));
        Assert.Null(job.ReleasedAt); Assert.Null(job.CompletedAt); Assert.Equal(AssetStatus.Maintenance, asset.Status);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Cancelled), token));
        Assert.Equal(MaintenanceStatus.Cancelled, job.Status); Assert.Null(job.ReleasedAt); Assert.Equal(AssetStatus.Inspection, asset.Status);
    }
    [Fact]
    public async Task Invoice_only_zero_cost_should_remain_zero_when_nonfinancial_details_saved()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        var request = MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { PartsCost = 0, LabourCost = 0, UseDetailedCosts = false, ActualCost = 0 };
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id,request,token));
        Assert.Equal(0m,job.ActualCost);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id,request with { UseDetailedCosts = null },token));
        Assert.Equal(0m,job.ActualCost);
    }
    [Fact]
    public async Task Invoice_mode_cannot_discard_issued_stock_cost()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        var part = new CREMS.Api.Domain.Corporate.InventoryPart { PartNumber = "CHECK", Name = "Stock", BranchId = asset.BranchId, QuantityOnHand = 2, UnitCost = 10 };
        db.Add(part); using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        var inventory = new BusinessOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext };
        Assert.IsType<OkObjectResult>(await inventory.UsePart(job.Id, new(part.Id, 1), token));
        Assert.IsType<BadRequestObjectResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { UseDetailedCosts = false, ActualCost = 100 }, token));
        Assert.Equal(10m, job.PartsCost); Assert.Equal(10m, job.ActualCost); Assert.Equal(MaintenanceStatus.Open, job.Status);
    }

    [Fact]
    public async Task Expected_release_is_optional_persists_changes_and_does_not_release_the_asset()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        var estimate = new DateTimeOffset(2026, 10, 9, 14, 0, 0, TimeSpan.FromHours(12));
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null, ExpectedReleaseAt: estimate), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        Assert.Equal(estimate, job.ExpectedReleaseAt);
        var workspace = new MaintenanceWorkspaceController(db, new CurrentStaffScope(db), new MaintenanceJobsTests.MaintenanceAuthorization(db)) { ControllerContext = jobs.ControllerContext };
        var record = System.Text.Json.JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await workspace.Detail(job.Id,token)).Value).GetProperty("job");
        Assert.Equal(estimate.UtcDateTime, record.GetProperty("expectedReleaseAt").GetDateTimeOffset().UtcDateTime);
        var elapsed = DateTimeOffset.UtcNow.AddDays(-1);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { ExpectedReleaseAt = elapsed },token));
        db.ChangeTracker.Clear();
        Assert.Equal(elapsed, (await db.MaintenanceJobs.SingleAsync(token)).ExpectedReleaseAt);
        Assert.Equal(AssetStatus.Maintenance, (await db.Assets.SingleAsync(token)).Status);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with { ExpectedReleaseAt = elapsed },token));
        Assert.Null((await db.MaintenanceJobs.SingleAsync(token)).ReleasedAt);
        Assert.Equal(AssetStatus.Inspection, (await db.Assets.SingleAsync(token)).Status);
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with { ExpectedReleaseAt = null },token));
        db.ChangeTracker.Clear();
        Assert.Null((await db.MaintenanceJobs.SingleAsync(token)).ExpectedReleaseAt);
        Assert.Equal(AssetStatus.Inspection, (await db.Assets.SingleAsync(token)).Status);
    }

}
