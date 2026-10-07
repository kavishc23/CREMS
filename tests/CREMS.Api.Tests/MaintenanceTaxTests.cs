using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class MaintenanceTaxTests
{
    [Theory]
    [InlineData(MaintenanceTaxMode.Exclusive, 68.75, 618.75)]
    [InlineData(MaintenanceTaxMode.Inclusive, 61.11, 550)]
    [InlineData(MaintenanceTaxMode.None, 0, 550)]
    [InlineData(MaintenanceTaxMode.Manual, 70, 620)]
    public async Task Server_calculates_and_persists_tax_modes(MaintenanceTaxMode mode, double tax, double total)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database();
        var (controller, asset) = await MaintenanceJobsTests.Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        var request = MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with {
            PartsCost = 500, LabourCost = 50, TaxCost = 70, UseDetailedCosts = true,
            TaxMode = mode, TaxRate = 12.5m, TaxableCosts = MaintenanceTaxableCosts.Parts | MaintenanceTaxableCosts.Labour,
            TaxOverrideReason = "Match supplier invoice rounding" };
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request, token));
        db.ChangeTracker.Clear();
        var saved = await db.MaintenanceJobs.SingleAsync(token);
        Assert.Equal((decimal)tax, saved.TaxCost); Assert.Equal((decimal)total, saved.ActualCost);
        Assert.Equal(mode, saved.TaxMode); Assert.Equal(12.5m, saved.TaxRate);
        Assert.Equal(mode == MaintenanceTaxMode.Manual ? request.TaxOverrideReason : null, saved.TaxOverrideReason);
        Assert.Contains(await db.AuditEvents.ToListAsync(token), audit => audit.Action == "Maintenance tax updated");
    }

    [Fact]
    public async Task Manual_override_requires_reason_and_invoice_only_does_not_add_tax_again()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (controller, asset) = await MaintenanceJobsTests.Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        var request = MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { TaxMode = MaintenanceTaxMode.Manual, TaxCost = 5 };
        Assert.IsType<BadRequestObjectResult>(await controller.Update(job.Id, request, token));
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request with { TaxOverrideReason = "Supplier invoice" }, token));
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request with { PartsCost = 0, LabourCost = 0, UseDetailedCosts = false, ActualCost = 55 }, token));
        Assert.Equal(55m, job.ActualCost); Assert.Equal(0m, job.TaxCost); Assert.Equal(MaintenanceTaxMode.None, job.TaxMode);
    }

    [Theory]
    [InlineData(MaintenanceTaxMode.Exclusive, 50, 600)]
    [InlineData(MaintenanceTaxMode.Inclusive, 44.44, 550)]
    public async Task Issued_stock_and_internal_labour_are_excluded(MaintenanceTaxMode mode, double tax, double total)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (controller, asset) = await MaintenanceJobsTests.Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        var part = new InventoryPart { PartNumber = "STOCK", Name = "Stock", BranchId = asset.BranchId, QuantityOnHand = 3, UnitCost = 100 };
        db.InventoryParts.Add(part); using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        var inventory = new BusinessOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = controller.ControllerContext };
        await inventory.UsePart(job.Id, new(part.Id, 1), token);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with {
            PartsCost = 500, LabourCost = 50, TaxMode = mode, TaxRate = 12.5m, TaxableCosts = MaintenanceTaxableCosts.Parts }, token));
        Assert.Equal((decimal)tax, job.TaxCost); Assert.Equal((decimal)total, job.ActualCost);
        await inventory.UsePart(job.Id, new(part.Id, 1), token);
        Assert.Equal((decimal)tax, job.TaxCost); Assert.Equal((decimal)total + 100, job.ActualCost);
    }

    [Fact]
    public async Task Invalid_tax_flags_and_rates_are_rejected_without_mutation()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (controller, asset) = await MaintenanceJobsTests.Setup(db);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null), token);
        var job = await db.MaintenanceJobs.SingleAsync(token);
        var request = MaintenanceJobsTests.Request(MaintenanceStatus.InProgress);
        Assert.IsType<BadRequestObjectResult>(await controller.Update(job.Id, request with { TaxRate = -1 }, token));
        Assert.IsType<BadRequestObjectResult>(await controller.Update(job.Id, request with { TaxableCosts = (MaintenanceTaxableCosts)32 }, token));
        Assert.IsType<BadRequestObjectResult>(await controller.Update(job.Id, request with { PartsCost = 10.001m }, token));
        Assert.Equal(MaintenanceStatus.Open, job.Status);
    }

    [Fact]
    public void Tax_rounds_half_cents_away_from_zero()
    {
        var job = new MaintenanceJob { JobNumber = "ROUND", ServiceType = "Repair", PartsCost = 0.04m,
            TaxRate = 12.5m, TaxMode = MaintenanceTaxMode.Exclusive, TaxableCosts = MaintenanceTaxableCosts.Parts };
        MaintenanceCosts.Calculate(job, 0);
        Assert.Equal(0.01m, job.TaxCost); Assert.Equal(0.05m, job.ActualCost);
    }
}
