using System.Security.Claims;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class MaintenanceJobsTests
{
    [Theory]
    [InlineData(MaintenanceStatus.Completed, false, AssetStatus.Available)]
    [InlineData(MaintenanceStatus.Cancelled, false, AssetStatus.Available)]
    [InlineData(MaintenanceStatus.Completed, true, AssetStatus.Maintenance)]
    [InlineData(MaintenanceStatus.Cancelled, true, AssetStatus.Maintenance)]
    public async Task Closing_job_respects_other_repairs(MaintenanceStatus status, bool otherOpen, AssetStatus expected)
    {
        await using var db = Database();
        var (controller, asset) = await Setup(db);
        Assert.IsType<OkObjectResult>(await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 50, null), TestContext.Current.CancellationToken));
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        if (otherOpen) await controller.Create(new(asset.Id, "Service", "Second fault", null, null, 0, null), TestContext.Current.CancellationToken);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, Request(status), TestContext.Current.CancellationToken));
        Assert.Equal(expected, asset.Status);
        Assert.Equal(30m, job.ActualCost);
        Assert.Equal(status == MaintenanceStatus.Completed, job.CompletedAt.HasValue);
    }

    [Fact]
    public async Task Repeated_completion_does_not_duplicate_history_and_reopening_clears_completion()
    {
        await using var db = Database();
        var (controller, asset) = await Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), TestContext.Current.CancellationToken);
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        await controller.Update(job.Id, Request(MaintenanceStatus.Completed), TestContext.Current.CancellationToken);
        await controller.Update(job.Id, Request(MaintenanceStatus.Completed), TestContext.Current.CancellationToken);
        Assert.Single(await db.AssetMeterReadings.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await db.AssetLifecycleEvents.CountAsync(TestContext.Current.CancellationToken));
        await controller.Update(job.Id, Request(MaintenanceStatus.InProgress), TestContext.Current.CancellationToken);
        Assert.Null(job.CompletedAt);
        Assert.Equal(AssetStatus.Maintenance, asset.Status);
    }

    [Fact]
    public async Task Staff_cannot_modify_another_branch_asset()
    {
        await using var db = Database();
        var (controller, asset) = await Setup(db, administrator: false);
        Assert.IsType<ForbidResult>(await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), TestContext.Current.CancellationToken));
        Assert.Empty(await db.MaintenanceJobs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(AssetStatus.Available, asset.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Staff_cannot_read_or_update_job_outside_branch_or_division(bool otherBranch)
    {
        await using var db = Database();
        var (controller, asset) = await Setup(db, administrator: false);
        var user = await db.Users.SingleAsync(TestContext.Current.CancellationToken);
        asset.DivisionId = otherBranch ? user.DivisionId : Guid.NewGuid();
        if (!otherBranch) user.BranchId = asset.BranchId;
        var job = new MaintenanceJob { JobNumber = "OTHER-BRANCH", AssetId = asset.Id, Asset = asset,
            BranchId = asset.BranchId, ServiceType = "Service", FaultDescription = "Private fault", Status = MaintenanceStatus.Open };
        db.Add(job);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = Assert.IsType<OkObjectResult>(await controller.GetAll(TestContext.Current.CancellationToken));
        Assert.Equal("[]", System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.IsType<ForbidResult>(await controller.Update(job.Id, Request(MaintenanceStatus.Completed), TestContext.Current.CancellationToken));
        Assert.Equal(MaintenanceStatus.Open, job.Status);
        Assert.Null(job.CompletedAt);
    }

    private static UpdateMaintenanceJobRequest Request(MaintenanceStatus status) =>
        new(status, "Repair", "Fault", "Technician", null, 50, null, null, null, PartsCost: 10, LabourCost: 20, MeterReading: 100);

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(MaintenanceJobsController, Asset)> Setup(ApplicationDbContext db, bool administrator = true)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Maintenance tester", BranchId = Guid.NewGuid(), DivisionId = Guid.NewGuid() };
        var branch = new Branch { Code = "TEST", Name = "Test branch" };
        var asset = new Asset { AssetNumber = "TEST-1", Name = "Test asset", BranchId = branch.Id, Branch = branch };
        db.AddRange(user, branch, asset);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, administrator ? SystemRoles.Administrator : SystemRoles.MaintenanceOfficer)
        }, "test"));
        return (new MaintenanceJobsController(db, new CurrentStaffScope(db)) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
        }, asset);
    }
}
