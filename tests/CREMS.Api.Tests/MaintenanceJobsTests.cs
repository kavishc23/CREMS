using System.Security.Claims;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class MaintenanceJobsTests
{
    [Theory]
    [InlineData(MaintenanceStatus.Completed, false, AssetStatus.Inspection)]
    [InlineData(MaintenanceStatus.Cancelled, false, AssetStatus.Inspection)]
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

    [Theory]
    [InlineData(MaintenanceStatus.Completed)]
    [InlineData(MaintenanceStatus.InProgress)]
    [InlineData(MaintenanceStatus.Cancelled)]
    public async Task Denied_completion_permission_protects_completed_jobs(MaintenanceStatus target)
    {
        await using var db = Database();
        var (controller, asset) = await Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), TestContext.Current.CancellationToken);
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        await controller.Update(job.Id, Request(MaintenanceStatus.Completed), TestContext.Current.CancellationToken);
        db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = (await db.Users.SingleAsync(TestContext.Current.CancellationToken)).Id,
            Permission = SystemPermissions.MaintenanceComplete, IsGranted = false });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(await controller.Update(job.Id, Request(target), TestContext.Current.CancellationToken));
        Assert.Equal(MaintenanceStatus.Completed, job.Status);
    }

    [Fact]
    public async Task Denied_completion_permission_blocks_completion_but_allows_open_work()
    {
        await using var db = Database(); var (controller, asset) = await Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), TestContext.Current.CancellationToken);
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = (await db.Users.SingleAsync(TestContext.Current.CancellationToken)).Id,
            Permission = SystemPermissions.MaintenanceComplete, IsGranted = false });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ForbidResult>(await controller.Update(job.Id, Request(MaintenanceStatus.Completed), TestContext.Current.CancellationToken));
        Assert.Null(job.CompletedAt);
        Assert.Equal(AssetStatus.Maintenance, asset.Status);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, Request(MaintenanceStatus.InProgress), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Historical_notes_preserve_advanced_meter_and_current_schedule()
    {
        await using var db = Database(); var (controller, asset) = await Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), TestContext.Current.CancellationToken);
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        var request = Request(MaintenanceStatus.Completed) with { NextServiceDate = new DateOnly(2026, 11, 1) };
        await controller.Update(job.Id, request, TestContext.Current.CancellationToken);
        asset.CurrentMeterReading = 200; asset.NextServiceDate = new DateOnly(2027, 1, 1);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request with { CompletionNotes = "Corrected notes" }, TestContext.Current.CancellationToken));
        Assert.Equal(200m, asset.CurrentMeterReading);
        Assert.Equal(new DateOnly(2027, 1, 1), asset.NextServiceDate);
        Assert.Equal("Corrected notes", job.Description);
        Assert.Single(await db.AssetMeterReadings.ToListAsync(TestContext.Current.CancellationToken));
        Assert.IsType<BadRequestObjectResult>(await controller.Update(job.Id, request with { MeterReading = 150 }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Schedule_correction_only_updates_asset_for_latest_service(bool newerJob)
    {
        await using var db = Database(); var (controller, asset) = await Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), TestContext.Current.CancellationToken);
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        var original = new DateOnly(2026, 11, 1); var revised = new DateOnly(2026, 12, 1);
        var request = Request(MaintenanceStatus.Completed) with { NextServiceDate = original };
        await controller.Update(job.Id, request, TestContext.Current.CancellationToken);
        if (newerJob)
        {
            db.MaintenanceJobs.Add(new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "NEWER", ServiceType = "Service",
                Status = MaintenanceStatus.Completed, CompletedAt = job.CompletedAt!.Value.AddDays(1), NextServiceDate = original });
            using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request with { NextServiceDate = revised }, TestContext.Current.CancellationToken));
        Assert.Equal(newerJob ? original : revised, asset.NextServiceDate);
        Assert.Equal(revised, job.NextServiceDate);
    }

    [Fact]
    public async Task Clearing_detailed_costs_does_not_restore_previous_total_and_manual_totals_remain_supported()
    {
        await using var db = Database(); var (controller, asset) = await Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), TestContext.Current.CancellationToken);
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        await controller.Update(job.Id, Request(MaintenanceStatus.InProgress), TestContext.Current.CancellationToken);
        var zero = Request(MaintenanceStatus.InProgress) with { PartsCost = 0, LabourCost = 0, ActualCost = 30 };
        await controller.Update(job.Id, zero, TestContext.Current.CancellationToken);
        Assert.Equal(0m, job.ActualCost);
        await controller.Update(job.Id, zero with { UseDetailedCosts = true }, TestContext.Current.CancellationToken);
        Assert.Equal(0m, job.ActualCost);
        await controller.Update(job.Id, zero with { UseDetailedCosts = false, ActualCost = 45 }, TestContext.Current.CancellationToken);
        Assert.Equal(45m, job.ActualCost);
    }

    [Fact]
    public async Task Invalid_references_are_rejected_on_create_and_update_without_mutation()
    {
        await using var db = Database(); var (controller, asset) = await Setup(db);
        var create = new SaveMaintenanceJobRequest(asset.Id, "Repair", "Fault", null, null, 0, null);
        Assert.IsType<BadRequestObjectResult>(await controller.Create(create with { SupplierId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(create with { ParentFailureJobId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        Assert.Empty(db.MaintenanceJobs); Assert.Equal(AssetStatus.Available, asset.Status);
        await controller.Create(create, TestContext.Current.CancellationToken);
        var job = await db.MaintenanceJobs.SingleAsync(TestContext.Current.CancellationToken);
        Assert.IsType<BadRequestObjectResult>(await controller.Update(job.Id, Request(MaintenanceStatus.Completed) with { SupplierId = Guid.NewGuid() }, TestContext.Current.CancellationToken));
        Assert.IsType<BadRequestObjectResult>(await controller.Update(job.Id, Request(MaintenanceStatus.Completed) with { ParentFailureJobId = job.Id }, TestContext.Current.CancellationToken));
        Assert.Equal(MaintenanceStatus.Open, job.Status);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public async Task Previous_failure_must_belong_to_same_asset_and_cannot_create_cycle()
    {
        await using var db = Database(); var (controller, asset) = await Setup(db);
        var supplier = new Supplier { SupplierNumber = "SUP-1", Name = "Supplier" };
        var unrelated = new MaintenanceJob { JobNumber = "UNRELATED", AssetId = Guid.NewGuid(), BranchId = Guid.NewGuid(), ServiceType = "Repair" };
        db.AddRange(supplier, unrelated);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var create = new SaveMaintenanceJobRequest(asset.Id, "Repair", "Fault", null, null, 0, null, SupplierId: supplier.Id);
        Assert.IsType<BadRequestObjectResult>(await controller.Create(create with { ParentFailureJobId = unrelated.Id }, TestContext.Current.CancellationToken));
        Assert.IsType<OkObjectResult>(await controller.Create(create, TestContext.Current.CancellationToken));
        var first = await db.MaintenanceJobs.SingleAsync(x => x.AssetId == asset.Id, TestContext.Current.CancellationToken);
        Assert.IsType<OkObjectResult>(await controller.Create(create with { ParentFailureJobId = first.Id }, TestContext.Current.CancellationToken));
        var second = await db.MaintenanceJobs.SingleAsync(x => x.ParentFailureJobId == first.Id, TestContext.Current.CancellationToken);
        Assert.IsType<BadRequestObjectResult>(await controller.Update(first.Id, Request(MaintenanceStatus.InProgress) with { ParentFailureJobId = second.Id }, TestContext.Current.CancellationToken));
    }

    internal static UpdateMaintenanceJobRequest Request(MaintenanceStatus status) =>
        new(status, "Repair", "Fault", "Technician", null, 50, null, null, null, PartsCost: 10, LabourCost: 20, MeterReading: 100, CompletionNotes: "Repair completed and tested", TransitionReason: "Recorded status change");

    internal static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    internal static async Task<(MaintenanceJobsController, Asset)> Setup(ApplicationDbContext db, bool administrator = true)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Maintenance tester", BranchId = Guid.NewGuid(), DivisionId = Guid.NewGuid() };
        var branch = new Branch { Code = "TEST", Name = "Test branch" };
        var asset = new Asset { AssetNumber = "TEST-1", Name = "Test asset", BranchId = branch.Id, Branch = branch };
        db.AddRange(user, branch, asset);
        foreach (var permission in new[] { SystemPermissions.MaintenanceComplete, SystemPermissions.AssetsInspect, SystemPermissions.AssetsViewFinancials })
            db.RolePermissions.Add(new RolePermission { RoleName = administrator ? SystemRoles.Administrator : SystemRoles.MaintenanceOfficer, Permission = permission });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, administrator ? SystemRoles.Administrator : SystemRoles.MaintenanceOfficer)
        }, "test"));
        return (new MaintenanceJobsController(db, new CurrentStaffScope(db), new MaintenanceAuthorization(db)) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
        }, asset);
    }

    internal sealed class MaintenanceAuthorization(ApplicationDbContext db) : IAuthorizationService
    {
        public async Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            var context = new AuthorizationHandlerContext(requirements, user, resource);
            await new PermissionAuthorizationHandler(db).HandleAsync(context);
            return context.HasSucceeded ? AuthorizationResult.Success() : AuthorizationResult.Failed();
        }
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            Assert.Contains(policyName, SystemPermissions.All);
            return AuthorizeAsync(user, resource, [new PermissionRequirement(policyName)]);
        }
    }
}
