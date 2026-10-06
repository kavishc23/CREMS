using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace CREMS.Api.Tests;
public class MaintenanceHandoffTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Failed_inspection_can_progress_through_repair_and_release()
    {
        await using var db=MaintenanceJobsTests.Database(); var (jobs,a)=await MaintenanceJobsTests.Setup(db);
        var profile=new AssetProfilesController(db,new CurrentStaffScope(db)){ControllerContext=jobs.ControllerContext};
        await profile.Inspect(a.Id,new(null,null,InspectionStage.Maintenance,InspectionOutcome.Failed,null,null,null,null,null,null,null,null,null,"Brake failure"),Token);
        var j=await db.MaintenanceJobs.SingleAsync(Token);
        Assert.Equal("Brake failure",j.FaultDescription); Assert.Equal(AssetStatus.Maintenance,a.Status);
        Assert.IsType<NoContentResult>(await jobs.Update(j.Id,MaintenanceJobsTests.Request(MaintenanceStatus.InProgress),Token));
        Assert.IsType<NoContentResult>(await jobs.Update(j.Id,MaintenanceJobsTests.Request(MaintenanceStatus.Completed),Token));
        Assert.Equal(AssetStatus.Available,a.Status);
    }
    [Theory]
    [InlineData(1000,1)]
    [InlineData(1001,1)]
    public async Task Meter_interval_crossing_creates_repair_and_blocks_asset(int reading,int count)
    {
        await using var db=MaintenanceJobsTests.Database(); var (jobs,a)=await MaintenanceJobsTests.Setup(db);
        var service=new ServiceOffering{Code="TEST",Name="Test service",MaintenanceRulesJson="{\"meterInterval\":1000}"};
        db.ServiceOfferings.Add(service);a.ServiceOfferingId=service.Id;a.CurrentMeterReading=990;
        using(db.SuppressNotifications())await db.SaveChangesAsync(Token);
        var profile=new AssetProfilesController(db,new CurrentStaffScope(db)){ControllerContext=jobs.ControllerContext};
        await profile.Meter(a.Id,new(MeterType.Odometer,"km",reading,null,MeterReadingSource.Maintenance,null),Token);
        Assert.Equal(count,await db.MaintenanceJobs.CountAsync(Token));Assert.Equal(AssetStatus.Maintenance,a.Status);
    }
    [Fact]
    public async Task Issued_stock_cost_cannot_be_erased_by_fresh_job_edit()
    {
        await using var db=MaintenanceJobsTests.Database();var(jobs,a)=await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(a.Id,"Repair","Fault",null,null,0,null),Token);var j=await db.MaintenanceJobs.SingleAsync(Token);
        var p=new InventoryPart{PartNumber="P",Name="Part",BranchId=a.BranchId,QuantityOnHand=5,UnitCost=10};db.InventoryParts.Add(p);
        using(db.SuppressNotifications())await db.SaveChangesAsync(Token);
        var ops=new BusinessOperationsController(db,new CurrentStaffScope(db)){ControllerContext=jobs.ControllerContext};
        await ops.UsePart(j.Id,new(p.Id,1),Token);
        Assert.IsType<BadRequestObjectResult>(await jobs.Update(j.Id,MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with{PartsCost=0,LabourCost=0,UseDetailedCosts=true,ExpectedVersion=j.UpdatedAt},Token));
        Assert.Equal(10m,j.ActualCost);Assert.Equal(4,p.QuantityOnHand);Assert.Equal(10m,(await db.MaintenancePartUsages.SingleAsync(Token)).UnitCost);
    }
    [Fact]
    public async Task Preventive_completion_requires_future_schedule()
    {
        await using var db=MaintenanceJobsTests.Database();var(jobs,a)=await MaintenanceJobsTests.Setup(db);
        var now=DateTimeOffset.UtcNow; a.NextServiceDate=MaintenanceRules.LocalDate(now);
        using(db.SuppressNotifications())await db.SaveChangesAsync(Token);
        Assert.Equal(1,await MaintenanceRules.GeneratePreventiveJobsAsync(db,now,Token));var j=await db.MaintenanceJobs.SingleAsync(Token);
        Assert.IsType<BadRequestObjectResult>(await jobs.Update(j.Id,MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with{IsPreventive=true,NextServiceDate=j.NextServiceDate},Token));
        Assert.IsType<NoContentResult>(await jobs.Update(j.Id,MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with{IsPreventive=true,NextServiceDate=MaintenanceRules.LocalDate(now).AddDays(30)},Token));
        Assert.Equal(0,await MaintenanceRules.GeneratePreventiveJobsAsync(db,now,Token));
    }
    [Fact]
    public async Task Dashboard_excludes_other_division_maintenance_costs()
    {
        await using var db=MaintenanceJobsTests.Database();var(jobs,a)=await MaintenanceJobsTests.Setup(db,administrator:false);
        var user=await db.Users.SingleAsync(Token);user.BranchId=a.BranchId;a.DivisionId=Guid.NewGuid();
        db.MaintenanceJobs.Add(new MaintenanceJob{JobNumber="PRIVATE",AssetId=a.Id,Asset=a,BranchId=a.BranchId,ServiceType="Private",ActualCost=321});
        using(db.SuppressNotifications())await db.SaveChangesAsync(Token);
        ((System.Security.Claims.ClaimsIdentity)jobs.User.Identity!).AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,SystemRoles.BranchManager)); var dashboard=new DashboardController(db,new CurrentStaffScope(db)){ControllerContext=jobs.ControllerContext};
        var response=Assert.IsType<OkObjectResult>(await dashboard.GetOperations(Token));
        var json=System.Text.Json.JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(0m,json.GetProperty("currentCosts").GetDecimal());
    }
    [Fact]
    public async Task Cancellation_preserves_consumed_stock_cost_in_profitability()
    {
        await using var db=MaintenanceJobsTests.Database();var(jobs,a)=await MaintenanceJobsTests.Setup(db);
        await jobs.Create(new(a.Id,"Repair","Fault",null,null,0,null),Token);var j=await db.MaintenanceJobs.SingleAsync(Token);
        var p=new InventoryPart{PartNumber="P",Name="Part",BranchId=a.BranchId,QuantityOnHand=5,UnitCost=10};db.InventoryParts.Add(p);
        using(db.SuppressNotifications())await db.SaveChangesAsync(Token);
        var ops=new BusinessOperationsController(db,new CurrentStaffScope(db)){ControllerContext=jobs.ControllerContext};
        await ops.UsePart(j.Id,new(p.Id,1),Token);
        await jobs.Update(j.Id,MaintenanceJobsTests.Request(MaintenanceStatus.Cancelled) with{PartsCost=10,LabourCost=0},Token);
        var report=Assert.IsType<OkObjectResult>((await ops.AssetProfitability(Token)).Result);
        var json=System.Text.Json.JsonSerializer.SerializeToElement(report.Value);
        Assert.Equal(10m,json.GetProperty("assets")[0].GetProperty("maintenanceCost").GetDecimal());
        Assert.Equal(4,p.QuantityOnHand);Assert.Equal(10m,j.ActualCost);
    }
    [Fact]
    public async Task Completing_unrelated_repair_preserves_future_service_date()
    {
        await using var db=MaintenanceJobsTests.Database();var(jobs,a)=await MaintenanceJobsTests.Setup(db);
        a.NextServiceDate=new DateOnly(2027,1,1);
        using(db.SuppressNotifications())await db.SaveChangesAsync(Token);
        await jobs.Create(new(a.Id,"Repair","Fault",null,null,0,null),Token);var j=await db.MaintenanceJobs.SingleAsync(Token);
        await jobs.Update(j.Id,MaintenanceJobsTests.Request(MaintenanceStatus.Completed),Token);
        Assert.Equal(new DateOnly(2027,1,1),a.NextServiceDate);
    }}
