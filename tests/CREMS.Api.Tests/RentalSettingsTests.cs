using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class RentalSettingsTests
{
    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static AdministrationController Controller(ApplicationDbContext db) => new(db, new CurrentStaffScope(db), null!, null!)
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Theory]
    [InlineData("rentals.bookingPrefix", "hire", true)]
    [InlineData("rentals.agreementPrefix", "LEASE-1", true)]
    [InlineData("rentals.bookingPrefix", "123", false)]
    [InlineData("rentals.agreementPrefix", "TOO-LONG-PREFIX", false)]
    [InlineData("rentals.bookingPrefix", "A/B", false)]
    [InlineData("rentals.bookingPrefix", "", false)]
    [InlineData("security.lockoutMinutes", "1", true)]
    [InlineData("security.lockoutMinutes", "1440", true)]
    [InlineData("security.lockoutMinutes", "1441", false)]
    [InlineData("security.lockoutMinutes", "1.5", false)]
    [InlineData("security.maxFailedAttempts", "0", false)]
    [InlineData("security.maxFailedAttempts", "3", true)]
    [InlineData("security.maxFailedAttempts", "101", false)]
    [InlineData("data.retentionYears", "10", true)]
    [InlineData("data.retentionYears", "0", false)]
    [InlineData("data.retentionYears", "-7", false)]
    [InlineData("data.retentionYears", "7.5", false)]
    [InlineData("rentals.lateFeeMultiplier", "0", true)]
    [InlineData("rentals.lateFeeMultiplier", "1.5", true)]
    [InlineData("rentals.lateFeeMultiplier", "10", true)]
    [InlineData("rentals.lateFeeMultiplier", "11", false)]
    [InlineData("rentals.vatRate", "100", true)]
    [InlineData("rentals.vatRate", "NaN", false)]
    public async Task Save_validates_and_persists_each_setting(string key, string value, bool valid)
    {
        await using var db = Database();
        var result = await Controller(db).SaveSetting(key, new SettingRequest(value), TestContext.Current.CancellationToken);
        if (valid)
        {
            Assert.IsType<NoContentResult>(result);
            db.ChangeTracker.Clear();
            Assert.Equal(key.EndsWith("Prefix") ? value.ToUpperInvariant() : value, await RentalSettings.Read(db, key, TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Empty(await db.SystemSettings.ToListAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Missing_defaults_are_listed_and_unknown_settings_are_rejected()
    {
        await using var db = Database();
        var result = Assert.IsType<OkObjectResult>(await Controller(db).Settings(TestContext.Current.CancellationToken));
        Assert.Equal(7, JsonSerializer.SerializeToElement(result.Value).GetArrayLength());
        Assert.IsType<NotFoundResult>(await Controller(db).SaveSetting("unknown", new SettingRequest("1"), TestContext.Current.CancellationToken));
        Assert.Equal("BK", await RentalSettings.Read(db, "rentals.bookingPrefix", TestContext.Current.CancellationToken));
        db.SystemSettings.Add(new SystemSetting { Key="security.maxFailedAttempts",Value="corrupt",Category="Security" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal("5", await RentalSettings.Read(db, "security.maxFailedAttempts", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Prefix_changes_apply_to_new_unique_references_only()
    {
        await using var db = Database();
        var old = new Booking { BookingNumber="BK-EXISTING" };
        db.Bookings.Add(old); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Controller(db).SaveSetting("rentals.bookingPrefix",new SettingRequest("RENTAL"),TestContext.Current.CancellationToken);
        await Controller(db).SaveSetting("rentals.agreementPrefix",new SettingRequest("LEASE"),TestContext.Current.CancellationToken);
        var reference = await RentalSettings.Reference(db,false,TestContext.Current.CancellationToken);
        Assert.StartsWith("RENTAL-",reference);
        Assert.StartsWith("LEASE-",await RentalSettings.Reference(db,true,TestContext.Current.CancellationToken));
        Assert.NotEqual(reference,await RentalSettings.Reference(db,false,TestContext.Current.CancellationToken));
        Assert.Equal("BK-EXISTING",old.BookingNumber);
        await Controller(db).SaveSetting("rentals.bookingPrefix",new SettingRequest("ABCDEFGHIJKL"),TestContext.Current.CancellationToken);
        Assert.Equal(26,(await RentalSettings.Reference(db,false,TestContext.Current.CancellationToken)).Length);
    }

    [Fact]
    public async Task Saved_lockout_policy_controls_real_identity_password_failures_without_mutating_shared_options()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddAuthentication();
        services.AddDbContext<ApplicationDbContext>(options=>options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager();
        using var provider=services.BuildServiceProvider();
        using var scope=provider.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn=scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var shared=manager.Options;
        await Controller(db).SaveSetting("security.maxFailedAttempts",new SettingRequest("2"),TestContext.Current.CancellationToken);
        await Controller(db).SaveSetting("security.lockoutMinutes",new SettingRequest("7"),TestContext.Current.CancellationToken);
        manager.Options=await RentalSettings.LoginOptions(db,shared,TestContext.Current.CancellationToken);
        Assert.NotSame(shared,manager.Options);
        Assert.Equal(5,shared.Lockout.MaxFailedAccessAttempts);
        var user=new ApplicationUser { UserName="settings-test",Email="settings@example.test",FullName="Settings Test",LockoutEnabled=true };
        Assert.True((await manager.CreateAsync(user,"Valid-Pass123!")).Succeeded);
        Assert.False((await signIn.CheckPasswordSignInAsync(user,"wrong",true)).IsLockedOut);
        var now=DateTimeOffset.UtcNow;
        Assert.True((await signIn.CheckPasswordSignInAsync(user,"wrong",true)).IsLockedOut);
        Assert.InRange(user.LockoutEnd!.Value,now.AddMinutes(6.9),now.AddMinutes(7.1));
        var lockedUntil=user.LockoutEnd;
        await Controller(db).SaveSetting("security.lockoutMinutes",new SettingRequest("15"),TestContext.Current.CancellationToken);
        manager.Options=await RentalSettings.LoginOptions(db,shared,TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromMinutes(15),manager.Options.Lockout.DefaultLockoutTimeSpan);
        Assert.Equal(lockedUntil,user.LockoutEnd);
    }

    [Fact]
    public async Task Retention_flags_old_closed_records_excludes_active_or_recent_records_and_deletes_nothing()
    {
        await using var db=Database();
        var old=DateTimeOffset.UtcNow.AddYears(-8);
        db.Bookings.AddRange(
            new Booking {BookingNumber="OLD-CLOSED",Status=BookingStatus.Completed,CreatedAt=old,UpdatedAt=old},
            new Booking {BookingNumber="OLD-ACTIVE",Status=BookingStatus.ConvertedToRental,CreatedAt=old},
            new Booking {BookingNumber="UPDATED",Status=BookingStatus.Completed,CreatedAt=old,UpdatedAt=DateTimeOffset.UtcNow},
            new Booking {BookingNumber="RECENT-END",Status=BookingStatus.Completed,CreatedAt=old,Items=[new BookingItem {EndAt=DateTimeOffset.UtcNow}]} );
        db.AuditEvents.Add(new AuditEvent {UserName="Admin",Action="Old event",EntityType="Booking",Summary="Test",OccurredAt=old});
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result=Assert.IsType<OkObjectResult>(await Controller(db).RetentionReview(token: TestContext.Current.CancellationToken));
        var json=JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(1,json.GetProperty("bookingCount").GetInt32());
        Assert.Equal(1,json.GetProperty("auditCount").GetInt32());
        await Controller(db).SaveSetting("data.retentionYears",new SettingRequest("10"),TestContext.Current.CancellationToken);
        result=Assert.IsType<OkObjectResult>(await Controller(db).RetentionReview(token: TestContext.Current.CancellationToken));
        json=JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(0,json.GetProperty("bookingCount").GetInt32());
        Assert.Equal(0,json.GetProperty("auditCount").GetInt32());
        Assert.Equal(4,await db.Bookings.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1,await db.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task Retention_review_paginates_without_hiding_or_deleting_history()
    {
        await using var db=Database();
        var old=DateTimeOffset.UtcNow.AddYears(-8);
        for(var i=0;i<26;i++) db.AuditEvents.Add(new AuditEvent {UserName="Admin",Action=$"Event {i}",EntityType="Booking",Summary="Test",OccurredAt=old.AddDays(i)});
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var first=JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await Controller(db).RetentionReview(1,TestContext.Current.CancellationToken)).Value);
        var second=JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await Controller(db).RetentionReview(2,TestContext.Current.CancellationToken)).Value);
        Assert.Equal(26,first.GetProperty("auditCount").GetInt32());
        Assert.Equal(25,first.GetProperty("audits").GetArrayLength());
        Assert.Equal(1,second.GetProperty("audits").GetArrayLength());
        Assert.Equal(26,await db.AuditEvents.CountAsync(TestContext.Current.CancellationToken));
    }

}
