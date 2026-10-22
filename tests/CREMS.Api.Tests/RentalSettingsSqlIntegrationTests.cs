using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class RentalSettingsSqlIntegrationTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("CREMS_RUN_SETTINGS_SQL_TESTS") == "1";

    [Fact(SkipUnless=nameof(Enabled),Skip="Enable CREMS_RUN_SETTINGS_SQL_TESTS for isolated SQL/HTTP settings verification.")]
    public async Task All_settings_save_through_authorized_HTTP_and_drive_SQL_backed_behavior()
    {
        var token=TestContext.Current.CancellationToken;
        var config=new ConfigurationBuilder().AddUserSecrets(typeof(ApplicationDbContext).Assembly,optional:true).Build();
        var source=Environment.GetEnvironmentVariable("CREMS_TEST_SQL_CONNECTION")??config.GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(source));
        var database="CremsSettingsTest_"+Guid.NewGuid().ToString("N");
        var connection=new SqlConnectionStringBuilder(source){InitialCatalog=database}.ConnectionString;
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        WebApplication? app=null;
        try
        {
            await db.Database.MigrateAsync(token);
            var branch=new Branch {Code="SETTINGS",Name="Settings test"};
            var division=new Division {Code="SETTINGS",Name="Settings test",DefaultTaxRate=12.5m};
            var customer=new CREMS.Api.Domain.Customers.Customer {CustomerNumber="SETTINGS-1",Name="Settings test customer",Email="settings@example.test"};
            var user=new ApplicationUser {UserName="settings-admin",FullName="Settings admin",IsActive=true};
            var old=new Booking {BookingNumber="EXISTING",BranchId=branch.Id,CustomerId=customer.Id,Status=BookingStatus.Completed,TaxRate=12.5m,CreatedAt=DateTimeOffset.UtcNow.AddYears(-8)};
            db.AddRange(branch,division,customer,user,old);
            using(db.SuppressNotifications())await db.SaveChangesAsync(token);
            // SaveChanges timestamps creation; set historical dates after insertion for the review fixture.
            await db.Bookings.Where(x=>x.Id==old.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CreatedAt,DateTimeOffset.UtcNow.AddYears(-8)).SetProperty(x=>x.UpdatedAt,(DateTimeOffset?)null),token);
            var builder=WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
            builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(connection));
            builder.Services.AddScoped<CurrentStaffScope>();
            builder.Services.AddScoped<IAuthorizationHandler,PermissionAuthorizationHandler>();
            builder.Services.AddApplicationAuthorization();
            builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme,o=>{
                o.Events.OnRedirectToLogin=c=>{c.Response.StatusCode=401;return Task.CompletedTask;};
                o.Events.OnRedirectToAccessDenied=c=>{c.Response.StatusCode=403;return Task.CompletedTask;};
            });
            builder.Services.AddControllers().AddApplicationPart(typeof(AdministrationController).Assembly);
            app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.MapControllers();
            app.MapPost("/test-session/{role}",async(HttpContext context,string role)=>await context.SignInAsync(IdentityConstants.ApplicationScheme,new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Role,role)
            ],IdentityConstants.ApplicationScheme))));
            await app.StartAsync(token);
            using var client=new HttpClient(new HttpClientHandler {CookieContainer=new CookieContainer(),AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/administration/settings",token)).StatusCode);
            (await client.PostAsync("/test-session/"+SystemRoles.RentalOfficer,null,token)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PutAsJsonAsync("/api/administration/settings/rentals.vatRate",new {value="20"},token)).StatusCode);
            (await client.PostAsync("/test-session/"+SystemRoles.SuperAdministrator,null,token)).EnsureSuccessStatusCode();
            var changes=new Dictionary<string,string>{["rentals.bookingPrefix"]="hire",["rentals.agreementPrefix"]="LEASE",["rentals.vatRate"]="15",["rentals.lateFeeMultiplier"]="1.5",["security.lockoutMinutes"]="9",["security.maxFailedAttempts"]="3",["data.retentionYears"]="7"};
            foreach(var (key,value) in changes)
                Assert.Equal(HttpStatusCode.NoContent,(await client.PutAsJsonAsync("/api/administration/settings/"+key,new {value},token)).StatusCode);
            var saved=await client.GetFromJsonAsync<JsonElement>("/api/administration/settings",token);
            Assert.Equal(7,saved.GetArrayLength());
            foreach(var (key,value) in changes)
                Assert.Equal(key.EndsWith("Prefix")?value.ToUpperInvariant():value,saved.EnumerateArray().Single(x=>x.GetProperty("key").GetString()==key).GetProperty("value").GetString());
            foreach(var (key,value) in new Dictionary<string,string>{["rentals.bookingPrefix"]="1BAD",["rentals.agreementPrefix"]="BAD PREFIX",["rentals.vatRate"]="101",["rentals.lateFeeMultiplier"]="-1",["security.lockoutMinutes"]="0",["security.maxFailedAttempts"]="1.5",["data.retentionYears"]="0"})
                Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync("/api/administration/settings/"+key,new {value},token)).StatusCode);
            db.ChangeTracker.Clear();
            Assert.Equal(15m,(await db.Divisions.SingleAsync(token)).DefaultTaxRate);
            Assert.Equal(12.5m,(await db.Bookings.SingleAsync(token)).TaxRate);
            Assert.StartsWith("HIRE-",await RentalSettings.Reference(db,false,token));
            Assert.StartsWith("LEASE-",await RentalSettings.Reference(db,true,token));
            var options=await RentalSettings.LoginOptions(db,new IdentityOptions(),token);
            Assert.Equal(3,options.Lockout.MaxFailedAccessAttempts);Assert.Equal(TimeSpan.FromMinutes(9),options.Lockout.DefaultLockoutTimeSpan);
            var review=await client.GetFromJsonAsync<JsonElement>("/api/administration/retention-review",token);
            Assert.Equal(1,review.GetProperty("bookingCount").GetInt32());
            Assert.Equal(7,await db.AuditEvents.CountAsync(x=>x.Action=="System setting updated",token));
            Assert.Equal(HttpStatusCode.NoContent,(await client.PutAsJsonAsync("/api/administration/settings/data.retentionYears",new {value="10"},token)).StatusCode);
            review=await client.GetFromJsonAsync<JsonElement>("/api/administration/retention-review",token);
            Assert.Equal(0,review.GetProperty("bookingCount").GetInt32());
            Assert.Equal(1,await db.Bookings.CountAsync(token));
        }
        finally
        {
            if(app is not null){await app.StopAsync(CancellationToken.None);await app.DisposeAsync();}
            if(new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog!=database || !database.StartsWith("CremsSettingsTest_"))throw new InvalidOperationException("Refusing to remove a non-test database.");
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }
}
