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

}
