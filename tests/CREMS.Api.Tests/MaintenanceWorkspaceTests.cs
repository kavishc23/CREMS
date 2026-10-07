using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public class MaintenanceWorkspaceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static MaintenanceWorkspaceController Workspace(ApplicationDbContext db, MaintenanceJobsController jobs) =>
        new(db, new CurrentStaffScope(db), new MaintenanceJobsTests.MaintenanceAuthorization(db)) { ControllerContext = jobs.ControllerContext };
    private static ReleaseMaintenanceRequest Release(MaintenanceJob job, Asset asset) => new(job.UpdatedAt ?? job.CreatedAt, "Repair verified and test run passed", asset.CurrentMeterReading, MaintenanceWorkspace.Checks(asset, null));

    [Fact]
    public async Task Completion_requires_notes_and_meter_then_release_requires_passed_checks_and_current_version()
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        asset.MeterUnit = "km"; asset.CurrentMeterReading = 50;
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), Token);
        var job = await db.MaintenanceJobs.SingleAsync(Token); var workspace = Workspace(db, jobs);
        Assert.IsType<BadRequestObjectResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with { CompletionNotes = null }, Token));
        Assert.IsType<BadRequestObjectResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with { MeterReading = null }, Token));
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed), Token));
        Assert.Equal(AssetStatus.Inspection, asset.Status); Assert.Null(job.ReleasedAt);
        Assert.IsType<BadRequestObjectResult>(await workspace.Release(job.Id, Release(job, asset) with { PassedChecks = [] }, Token));
        Assert.IsType<ConflictObjectResult>(await workspace.Release(job.Id, Release(job, asset) with { ExpectedVersion = job.CreatedAt.AddYears(-1) }, Token));
        Assert.IsType<OkObjectResult>(await workspace.Release(job.Id, Release(job, asset), Token));
        Assert.Equal(AssetStatus.Available, asset.Status); Assert.NotNull(job.ReleaseInspectionId);
        Assert.Equal(InspectionOutcome.Passed, (await db.AssetInspections.SingleAsync(Token)).Outcome);
        Assert.IsType<ConflictObjectResult>(await workspace.Release(job.Id, Release(job, asset), Token));
    }

    [Theory]
    [InlineData("job")]
    [InlineData("booking")]
    [InlineData("transfer")]
    [InlineData("inspection")]
    [InlineData("meter")]
    public async Task Safety_release_respects_every_operational_blocker(string blocker)
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), Token);
        var job = await db.MaintenanceJobs.SingleAsync(Token);
        await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed), Token);
        if (blocker == "job") db.MaintenanceJobs.Add(new MaintenanceJob { JobNumber = "OTHER", AssetId = asset.Id, BranchId = asset.BranchId, ServiceType = "Repair" });
        if (blocker == "booking") { var booking = new Booking { BookingNumber = "HIRE", Status = BookingStatus.ConvertedToRental, BranchId = asset.BranchId }; db.Add(new BookingItem { Booking = booking, BookingId = booking.Id, AssetId = asset.Id, StartAt = DateTimeOffset.UtcNow.AddDays(-2), EndAt = DateTimeOffset.UtcNow.AddDays(-1) }); }
        if (blocker == "transfer") db.AssetTransfers.Add(new AssetTransfer { TransferNumber = "TRANSFER", Reason = "Test transfer", AssetId = asset.Id, FromBranchId = asset.BranchId, ToBranchId = Guid.NewGuid(), Status = TransferStatus.InTransit });
        if (blocker == "inspection") db.AssetInspections.Add(new AssetInspection { CompletedByName = "Inspector", AssetId = asset.Id, Stage = InspectionStage.Maintenance, Outcome = InspectionOutcome.Failed, CompletedAt = DateTimeOffset.UtcNow.AddMinutes(1) });
        if (blocker == "meter") job.NextServiceMeter = 90;
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        Assert.IsType<BadRequestObjectResult>(await Workspace(db, jobs).Release(job.Id, Release(job, asset), Token));
        Assert.Equal(AssetStatus.Inspection, asset.Status); Assert.Null(job.ReleasedAt);
    }

    [Fact]
    public async Task Release_permission_and_branch_scope_are_enforced()
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), Token);
        var job = await db.MaintenanceJobs.SingleAsync(Token); await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed), Token);
        var user = await db.Users.SingleAsync(Token);
        db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = user.Id, Permission = SystemPermissions.AssetsInspect, IsGranted = false });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        Assert.IsType<ForbidResult>(await Workspace(db, jobs).Release(job.Id, Release(job, asset), Token));
        var (otherStaff, _) = await MaintenanceJobsTests.Setup(db, administrator: false);
        Assert.IsType<ForbidResult>(await Workspace(db, otherStaff).Release(job.Id, Release(job, asset), Token));
        Assert.IsType<NotFoundResult>(await Workspace(db, otherStaff).Detail(job.Id, Token));
    }

    [Fact]
    public async Task Denied_financial_permission_omits_fields_and_preserves_saved_costs()
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 40, null), Token);
        var job = await db.MaintenanceJobs.SingleAsync(Token); await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { LabourHours = 2, LabourRate = 25, FuelCost = 5 }, Token);
        var user = await db.Users.SingleAsync(Token); db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = user.Id, Permission = SystemPermissions.AssetsViewFinancials, IsGranted = false });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var detail = Assert.IsType<OkObjectResult>(await Workspace(db, jobs).Detail(job.Id, Token)); var json = JsonSerializer.SerializeToElement(detail.Value);
        foreach (var field in MaintenanceWorkspace.FinancialFields) Assert.False(json.GetProperty("job").TryGetProperty(field, out _));
        Assert.False(json.GetProperty("metrics").TryGetProperty("totalCost", out _)); Assert.False(json.GetProperty("metrics").TryGetProperty("totalRecordedCost", out _));
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { ActualCost = 999, LabourCost = 999, FuelCost = 999 }, Token));
        Assert.Equal(65m, job.ActualCost); Assert.Equal(50m, job.LabourCost); Assert.Equal(5m, job.FuelCost);
        var inventory = new InventoryPart { PartNumber = "FILTER", Name = "Filter", BranchId = asset.BranchId, QuantityOnHand = 5, UnitCost = 10 };
        db.Add(inventory); using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var operations = new BusinessOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext };
        var issued = Assert.IsType<OkObjectResult>(await operations.UsePart(job.Id, new(inventory.Id, 1), Token));
        Assert.False(JsonSerializer.SerializeToElement(issued.Value).TryGetProperty("unitCost", out _));
        var profile = new AssetProfilesController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext };
        var assetHistory = Assert.IsType<OkObjectResult>(await profile.Profile(asset.Id, Token));
        Assert.False(JsonSerializer.SerializeToElement(assetHistory.Value).GetProperty("maintenance")[0].TryGetProperty("actualCost", out _));
    }

    [Fact]
    public async Task Monthly_targets_and_missing_estimates_are_explicit()
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(asset.Id, "Service", "Service due", null, null, 0, null, IsPreventive: true, ServiceIntervalMonths: 3, HasEstimate: false), Token);
        var job = await db.MaintenanceJobs.SingleAsync(Token); Assert.False(job.HasEstimate);
        await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with { IsPreventive = true, ServiceIntervalMonths = 3, HasEstimate = true, EstimatedCost = 0 }, Token);
        Assert.True(job.HasEstimate); Assert.Equal(0m, job.EstimatedCost);
        Assert.Equal(MaintenanceRules.LocalDate(DateTimeOffset.UtcNow).AddMonths(3), asset.NextServiceDate);
        await Workspace(db, jobs).Release(job.Id, Release(job, asset), Token);
        Assert.Equal(1, await MaintenanceRules.GeneratePreventiveJobsAsync(db, DateTimeOffset.UtcNow.AddMonths(4), Token));
        var next = await db.MaintenanceJobs.SingleAsync(x => x.Status == MaintenanceStatus.Open, Token);
        Assert.Equal(3, next.ServiceIntervalMonths); Assert.Null(next.NextServiceDate);
    }

    [Fact]
    public void Sectioned_inspection_templates_supply_required_checks()
    {
        var asset = new Asset { AssetNumber = "A", Name = "A" };
        Assert.Equal(new[] { "Brakes", "Hydraulics" }, MaintenanceWorkspace.Checks(asset, "[{\"section\":\"Safety\",\"items\":[{\"label\":\"Brakes\"},{\"label\":\"Hydraulics\"}]}]"));
    }
    [Theory]
    [InlineData(999, false, false)]
    [InlineData(1000, true, false)]
    [InlineData(1001, true, true)]
    public async Task Initial_meter_rule_schedule_due_counts_match_automatic_jobs(int reading, bool due, bool overdue)
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        var service = new CREMS.Api.Domain.Common.ServiceOffering { Code = "INITIAL", Name = "Initial service", MaintenanceRulesJson = "{\"meterInterval\":1000}" };
        db.Add(service); asset.ServiceOfferingId = service.Id; asset.NextServiceDate = null; asset.CurrentMeterReading = reading; asset.MeterUnit = "km";
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var result = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await Workspace(db, jobs).Schedule(token: Token)).Value);
        Assert.Equal(due ? 1 : 0, result.GetProperty("dueCount").GetInt32());
        Assert.Equal(overdue ? 1 : 0, result.GetProperty("overdueCount").GetInt32());
        Assert.Equal(1000, result.GetProperty("items")[0].GetProperty("NextServiceMeter").GetDecimal());
        var overdueResult = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await Workspace(db, jobs).Schedule(overdueOnly: true, token: Token)).Value);
        Assert.Equal(overdue ? 1 : 0, overdueResult.GetProperty("total").GetInt32());
        Assert.Equal(due ? 1 : 0, await MaintenanceRules.GeneratePreventiveJobsAsync(db, DateTimeOffset.UtcNow, Token));
        Assert.Equal(0, await MaintenanceRules.GeneratePreventiveJobsAsync(db, DateTimeOffset.UtcNow, Token));
    }

    [Theory]
    [InlineData("{\"meterInterval\":0}")]
    [InlineData("not json")]
    [InlineData("{\"unimplementedRule\":3}")]
    public void Invalid_or_unsupported_rules_have_visible_warnings(string json)
    {
        var (interval, warning) = MaintenanceRules.ReadMeterRule(json);
        Assert.Null(interval); Assert.NotNull(warning);
    }

    [Fact]
    public async Task Repair_deadline_filter_includes_safety_pending_but_excludes_cancelled_or_released_work()
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        foreach (var status in new[] { MaintenanceStatus.Open, MaintenanceStatus.Completed, MaintenanceStatus.Cancelled })
            db.Add(new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "DEADLINE-" + status, ServiceType = "Repair", Status = status, ExpectedReleaseAt = DateTimeOffset.UtcNow.AddDays(-1) });
        db.Add(new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "RELEASED", ServiceType = "Repair", Status = MaintenanceStatus.Completed, ExpectedReleaseAt = DateTimeOffset.UtcNow.AddDays(-1), ReleasedAt = DateTimeOffset.UtcNow });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var result = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await Workspace(db, jobs).List(queue: "History", overdueRepairs: true, token: Token)).Value);
        Assert.Equal(2, result.GetProperty("total").GetInt32()); Assert.Equal(2, result.GetProperty("counts").GetProperty("overdueRepairs").GetInt32());
    }

    [Fact]
    public async Task Inspection_record_navigation_is_scoped_and_does_not_expose_signature_or_raw_evidence()
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        var inspection = new AssetInspection { AssetId = asset.Id, CompletedByName = "Inspector", Notes = "Fault verified", ResponsesJson = "[{\"label\":\"Leak test\",\"passed\":false}]", StaffSignatureDataUrl = "private-signature" };
        db.Add(inspection); using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var result = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await Workspace(db, jobs).InspectionRecord(inspection.Id, Token)).Value);
        Assert.Equal("Fault verified", result.GetProperty("Notes").GetString()); Assert.False(result.TryGetProperty("StaffSignatureDataUrl", out _));
        var (other, _) = await MaintenanceJobsTests.Setup(db, administrator: false);
        Assert.IsType<NotFoundResult>(await Workspace(db, other).InspectionRecord(inspection.Id, Token));
    }

    [Fact]
    public async Task Recorded_actual_cost_includes_cancelled_and_active_work_without_counting_estimates()
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        asset.Status = AssetStatus.Maintenance;
        var completed = new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "COST-COMPLETE", ServiceType = "Repair", Status = MaintenanceStatus.Completed, ActualCost = 10, HasEstimate = true, EstimatedCost = 1000 };
        db.AddRange(completed,
            new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "COST-CANCEL", ServiceType = "Repair", Status = MaintenanceStatus.Cancelled, ActualCost = 20 },
            new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "COST-ACTIVE", ServiceType = "Repair", Status = MaintenanceStatus.InProgress, ActualCost = 30 },
            new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "ESTIMATE-ONLY", ServiceType = "Repair", Status = MaintenanceStatus.Open, HasEstimate = true, EstimatedCost = 500 });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var result = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await Workspace(db, jobs).Detail(completed.Id, Token)).Value).GetProperty("metrics");
        Assert.Equal(60, result.GetProperty("totalRecordedCost").GetDecimal()); Assert.Equal(10, result.GetProperty("totalCost").GetDecimal());
    }

}
