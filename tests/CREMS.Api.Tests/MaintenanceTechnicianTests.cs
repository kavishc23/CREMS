using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class MaintenanceTechnicianTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Personnel Technician(Asset asset, string number, decimal rate) => new() { EmployeeNumber = number, FullName = number, BranchId = asset.BranchId, DivisionId = asset.DivisionId ??= Guid.NewGuid(), Type = PersonnelType.Technician, StandardCostRate = rate };

    [Fact]
    public async Task Assignment_snapshots_rate_and_server_calculates_labour_without_repricing_old_jobs()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, asset) = await MaintenanceJobsTests.Setup(db);
        var first = Technician(asset, "TECH-1", 25); var second = Technician(asset, "TECH-2", 40); db.AddRange(first, second); await db.SaveChangesAsync(Token);
        Assert.IsType<OkObjectResult>(await controller.Create(new(asset.Id, "Repair", "Fault", "Forged name", null, 0, null, AssignedPersonnelId: first.Id), Token));
        var job = await db.MaintenanceJobs.SingleAsync(Token);
        Assert.Equal(25m, job.LabourRate); Assert.Equal(first.FullName, job.AssignedTo); Assert.Null(job.LabourHours);
        var request = MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { AssignedPersonnelId = first.Id, LabourHours = 2, LabourRate = 999 };
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request, Token)); Assert.Equal(50m, job.LabourCost);
        first.StandardCostRate = 55; await db.SaveChangesAsync(Token);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request with { LabourHours = 3 }, Token)); Assert.Equal(25m, job.LabourRate); Assert.Equal(75m, job.LabourCost);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request with { AssignedPersonnelId = second.Id }, Token)); Assert.Equal(40m, job.LabourRate); Assert.Equal(80m, job.LabourCost);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request with { AssignedPersonnelId = second.Id, LabourHours = null, LabourRate = null }, Token)); Assert.Equal(40m, job.LabourRate);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, request with { AssignedPersonnelId = second.Id, UseDetailedCosts = false, ActualCost = 100 }, Token)); Assert.Equal(40m, job.LabourRate); Assert.Null(job.LabourHours); Assert.Equal(100m, job.ActualCost);
    }

    [Fact]
    public async Task Inactive_or_wrong_branch_technicians_cannot_be_new_assignments_but_existing_jobs_remain_editable()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, asset) = await MaintenanceJobsTests.Setup(db);
        var tech = Technician(asset, "TECH-1", 25); db.Add(tech); await db.SaveChangesAsync(Token);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null, AssignedPersonnelId: tech.Id), Token);
        var job = await db.MaintenanceJobs.SingleAsync(Token);
        tech.IsActive = false; tech.Availability = PersonnelAvailability.Leave; await db.SaveChangesAsync(Token);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { AssignedPersonnelId = tech.Id }, Token));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null, AssignedPersonnelId: tech.Id), Token));
        tech.IsActive = true; tech.Availability = PersonnelAvailability.Available; tech.BranchId = Guid.NewGuid(); await db.SaveChangesAsync(Token);
        Assert.IsType<BadRequestObjectResult>(await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null, AssignedPersonnelId: tech.Id), Token));
    }

    [Fact]
    public async Task Supplier_selection_uses_master_name_and_inactive_suppliers_are_retained_only_on_existing_jobs()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, asset) = await MaintenanceJobsTests.Setup(db);
        var supplier = new Supplier { SupplierNumber = "SUP-1", Name = "Fleet Services" }; db.Add(supplier); await db.SaveChangesAsync(Token);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, "Forged name", 0, null, SupplierId: supplier.Id), Token);
        var job = await db.MaintenanceJobs.SingleAsync(Token); Assert.Equal("Fleet Services", job.Supplier);
        supplier.IsActive = false; await db.SaveChangesAsync(Token);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { SupplierId = supplier.Id, Supplier = job.Supplier }, Token));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null, SupplierId: supplier.Id), Token));
    }

    [Fact]
    public async Task Options_hide_rates_without_financial_permission_but_assignment_still_uses_master_rate()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, asset) = await MaintenanceJobsTests.Setup(db);
        var tech = Technician(asset, "TECH-1", 32); db.Add(tech);
        db.RolePermissions.RemoveRange(db.RolePermissions.Where(x => x.Permission == SystemPermissions.AssetsViewFinancials)); await db.SaveChangesAsync(Token);
        var workspace = new MaintenanceWorkspaceController(db, new CurrentStaffScope(db), new MaintenanceJobsTests.MaintenanceAuthorization(db)) { ControllerContext = controller.ControllerContext };
        var json = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await workspace.Options(Token)).Value);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("technicians")[0].GetProperty("standardCostRate").ValueKind);
        await controller.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null, AssignedPersonnelId: tech.Id), Token);
        var job = await db.MaintenanceJobs.SingleAsync(Token); Assert.Equal(32m, job.LabourRate);
        Assert.IsType<NoContentResult>(await controller.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { AssignedPersonnelId = tech.Id, LabourRate = 999, LabourHours = 10 }, Token));
        Assert.Equal(32m, job.LabourRate); Assert.Null(job.LabourHours);
    }

    [Fact]
    public async Task Directory_edits_validate_unique_references_location_rate_and_stale_versions()
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        ((ClaimsIdentity)jobs.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, SystemRoles.SuperAdministrator));
        var division = new Division { Code = "MOTORS", Name = "Motors" }; db.Add(division); db.Add(new BranchDivision { BranchId = asset.BranchId, DivisionId = division.Id }); await db.SaveChangesAsync(Token);
        var directory = new MaintenanceDirectoryController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext };
        var request = new TechnicianDirectoryRequest("Ravi", asset.BranchId, division.Id, 30);
        Assert.IsType<OkObjectResult>(await directory.CreateTechnician(request, Token));
        Assert.IsType<OkObjectResult>(await directory.CreateTechnician(request with { FullName = "Ana" }, Token));
        var numbers = await db.Personnel.Select(x => x.EmployeeNumber).ToListAsync(Token);
        Assert.Equal(2, numbers.Distinct().Count());
        Assert.All(numbers, number => Assert.Matches("^EMP-[A-F0-9]{12}$", number));
        Assert.IsType<BadRequestObjectResult>(await directory.CreateTechnician(request with { StandardCostRate = -1 }, Token));
        Assert.IsType<BadRequestObjectResult>(await directory.CreateTechnician(request with { StandardCostRate = 1.234m }, Token));
        Assert.IsType<BadRequestObjectResult>(await directory.CreateTechnician(request with { DivisionId = Guid.NewGuid() }, Token));
        var tech = await db.Personnel.SingleAsync(x => x.FullName == "Ravi", Token);
        var originalNumber = tech.EmployeeNumber;
        Assert.IsType<ConflictObjectResult>(await directory.UpdateTechnician(tech.Id, request, Token));
        Assert.IsType<OkObjectResult>(await directory.UpdateTechnician(tech.Id, request with { StandardCostRate = 45, IsActive = false, ExpectedVersion = tech.UpdatedAt ?? tech.CreatedAt }, Token)); Assert.Equal(45m, tech.StandardCostRate); Assert.False(tech.IsActive); Assert.Equal(originalNumber, tech.EmployeeNumber);
        Assert.IsType<OkObjectResult>(await directory.CreateSupplier(new("SUP-1", "Fleet Services"), Token));
        Assert.IsType<ConflictObjectResult>(await directory.CreateSupplier(new("sup-1", "Duplicate"), Token));
        var supplier = await db.Suppliers.SingleAsync(Token);
        Assert.IsType<OkObjectResult>(await directory.UpdateSupplier(supplier.Id, new("SUP-1", "Fleet Services Updated", IsActive: false, ExpectedVersion: supplier.UpdatedAt ?? supplier.CreatedAt), Token)); Assert.False(supplier.IsActive);
        Assert.Equal(SystemPolicies.AdministerSystem, typeof(MaintenanceDirectoryController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.True(await db.AuditEvents.AnyAsync(x => x.Action == "Maintenance technician saved", Token));
    }
    [Fact]
    public async Task Multiple_locations_persist_and_only_exact_pairs_allow_new_jobs()
    {
        await using var db = MaintenanceJobsTests.Database(); var (jobs, asset) = await MaintenanceJobsTests.Setup(db);
        ((ClaimsIdentity)jobs.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, SystemRoles.SuperAdministrator));
        var division = new Division { Code = "ONE", Name = "One" };
        var otherDivision = new Division { Code = "TWO", Name = "Two" };
        var otherBranch = new Branch { Code = "OTHER", Name = "Other" };
        asset.DivisionId = division.Id;
        db.AddRange(division, otherDivision, otherBranch);
        db.AddRange(new BranchDivision { BranchId = asset.BranchId, DivisionId = division.Id }, new BranchDivision { BranchId = otherBranch.Id, DivisionId = otherDivision.Id });
        await db.SaveChangesAsync(Token);
        var directory = new MaintenanceDirectoryController(db, new CurrentStaffScope(db)) { ControllerContext = jobs.ControllerContext };
        var primary = new TechnicianLocationRequest(otherBranch.Id, otherDivision.Id);
        var secondary = new TechnicianLocationRequest(asset.BranchId, division.Id);
        var request = new TechnicianDirectoryRequest("Shared technician", primary.BranchId, primary.DivisionId, 37, Locations: [primary, secondary, secondary]);
        Assert.IsType<BadRequestObjectResult>(await directory.CreateTechnician(request with { Locations = [] }, Token));
        Assert.IsType<BadRequestObjectResult>(await directory.CreateTechnician(request with { Locations = [new(asset.BranchId, otherDivision.Id)] }, Token));
        Assert.IsType<OkObjectResult>(await directory.CreateTechnician(request, Token));
        db.ChangeTracker.Clear();
        var tech = await db.Personnel.Include(x => x.Locations).SingleAsync(Token);
        Assert.Equal(2, tech.Locations.Count);
        Assert.IsType<OkObjectResult>(await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null, AssignedPersonnelId: tech.Id), Token));
        var job = await db.MaintenanceJobs.SingleAsync(Token); Assert.Equal(37m, job.LabourRate);
        Assert.IsType<OkObjectResult>(await directory.UpdateTechnician(tech.Id, request with { Locations = [primary], ExpectedVersion = tech.UpdatedAt ?? tech.CreatedAt }, Token));
        Assert.Single(await db.PersonnelLocations.ToListAsync(Token));
        Assert.IsType<BadRequestObjectResult>(await jobs.Create(new(asset.Id, "Repair", "Fault", null, null, 0, null, AssignedPersonnelId: tech.Id), Token));
        Assert.IsType<NoContentResult>(await jobs.Update(job.Id, MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { AssignedPersonnelId = tech.Id }, Token));
        Assert.Equal(37m, job.LabourRate);
    }
}
