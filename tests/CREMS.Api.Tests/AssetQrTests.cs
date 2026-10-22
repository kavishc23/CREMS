using System.Security.Claims;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class AssetQrTests
{
    [Theory]
    [InlineData("  shp-suv-3001  ")]
    [InlineData("https://crems.example/staff/scan?code=shp-suv-3001")]
    public async Task Typed_codes_and_label_urls_return_live_asset_availability(string code)
    {
        await using var db=Database();var (controller,asset)=await Controller(db);
        var result=Assert.IsType<OkObjectResult>(await controller.Resolve(new ResolveQrRequest(code),TestContext.Current.CancellationToken));
        using var json=Json(result);
        Assert.Equal(asset.Id,json.RootElement.GetProperty("asset").GetProperty("id").GetGuid());
        Assert.True(json.RootElement.GetProperty("availability").GetProperty("availableNow").GetBoolean());
    }

    [Fact]
    public async Task Open_maintenance_blocks_an_asset_even_if_its_snapshot_says_available()
    {
        await using var db=Database();var (controller,asset)=await Controller(db);
        db.MaintenanceJobs.Add(new MaintenanceJob{JobNumber="MNT-2026-0021",AssetId=asset.Id,BranchId=asset.BranchId,ServiceType="Safety inspection",Status=MaintenanceStatus.InProgress});
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var json=Json(Assert.IsType<OkObjectResult>(await controller.Resolve(new ResolveQrRequest(asset.AssetNumber),TestContext.Current.CancellationToken)));
        Assert.False(json.RootElement.GetProperty("availability").GetProperty("availableNow").GetBoolean());
        Assert.Single(json.RootElement.GetProperty("availability").GetProperty("maintenance").EnumerateArray());
    }

    [Fact]
    public async Task A_future_reservation_is_shown_without_starting_an_early_pickup()
    {
        await using var db=Database();var (controller,asset)=await Controller(db);
        var customer=new Customer{CustomerNumber="CUS-2026-1001",Name="Pacific Site Services"};db.Customers.Add(customer);
        var booking=new Booking{BookingNumber="BK-2026-1001",BranchId=asset.BranchId,CustomerId=customer.Id,Status=BookingStatus.Confirmed};db.Bookings.Add(booking);
        db.BookingItems.Add(new BookingItem{BookingId=booking.Id,AssetId=asset.Id,StartAt=DateTimeOffset.UtcNow.AddDays(4),EndAt=DateTimeOffset.UtcNow.AddDays(7),DailyRate=35});
        asset.Status=AssetStatus.Reserved;await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var json=Json(Assert.IsType<OkObjectResult>(await controller.Resolve(new ResolveQrRequest(asset.AssetNumber),TestContext.Current.CancellationToken)));
        Assert.Equal("ViewOnly",json.RootElement.GetProperty("action").GetString());
        Assert.True(json.RootElement.GetProperty("availability").GetProperty("availableNow").GetBoolean());
        Assert.Single(json.RootElement.GetProperty("availability").GetProperty("upcoming").EnumerateArray());
    }

    [Fact]
    public async Task An_overdue_active_hire_remains_visible_and_blocks_availability()
    {
        await using var db=Database();var (controller,asset)=await Controller(db);
        var customer=new Customer{CustomerNumber="CUS-2026-1002",Name="Coral Coast Site Services"};db.Customers.Add(customer);
        var booking=new Booking{BookingNumber="BK-2026-1002",BranchId=asset.BranchId,CustomerId=customer.Id,Status=BookingStatus.ConvertedToRental};db.Bookings.Add(booking);
        db.BookingItems.Add(new BookingItem{BookingId=booking.Id,AssetId=asset.Id,StartAt=DateTimeOffset.UtcNow.AddDays(-5),EndAt=DateTimeOffset.UtcNow.AddDays(-1),DailyRate=35});
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var json=Json(Assert.IsType<OkObjectResult>(await controller.Resolve(new ResolveQrRequest(asset.AssetNumber),TestContext.Current.CancellationToken)));
        Assert.Equal("CheckIn",json.RootElement.GetProperty("action").GetString());
        Assert.False(json.RootElement.GetProperty("availability").GetProperty("availableNow").GetBoolean());
        Assert.Single(json.RootElement.GetProperty("availability").GetProperty("upcoming").EnumerateArray());
    }

    [Fact]
    public async Task Scan_cannot_reveal_an_asset_outside_the_staff_scope()
    {
        await using var db=Database();var (controller,asset)=await Controller(db,false);
        Assert.IsType<ForbidResult>(await controller.Resolve(new ResolveQrRequest(asset.AssetNumber),TestContext.Current.CancellationToken));
    }

    private static JsonDocument Json(OkObjectResult result)=>JsonDocument.Parse(JsonSerializer.Serialize(result.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    private static ApplicationDbContext Database()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<(AssetQrController Controller,Asset Asset)> Controller(ApplicationDbContext db,bool administrator=true)
    {
        var branch=new Branch{Code="SUV",Name="Suva"};var division=new Division{Code="SHIPPING",Name="Carpenters Shipping"};
        var user=new ApplicationUser{FullName="Rental officer",IsActive=true};
        var asset=new Asset{AssetNumber="SHP-SUV-3001",Name="Standard portable toilet",Type=AssetType.PortableSanitation,BranchId=branch.Id,DivisionId=division.Id};
        db.AddRange(branch,division,user,asset);using(db.SuppressNotifications())await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Role,administrator?SystemRoles.SuperAdministrator:SystemRoles.RentalOfficer)],"Test"));
        return (new AssetQrController(db,new CurrentStaffScope(db)){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext{User=principal}}},asset);
    }
}
