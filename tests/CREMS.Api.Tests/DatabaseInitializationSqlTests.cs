using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
namespace CREMS.Api.Tests;

public sealed class DatabaseInitializationSqlTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("CREMS_RUN_SQL_TESTS") == "1";
    [Fact(SkipUnless=nameof(Enabled),Skip="Enable isolated local SQL initialization verification.")]
    public async Task Development_initialization_creates_consistent_catalogue_and_repeated_startup_preserves_counts()
    {
        var token=TestContext.Current.CancellationToken;
        var config=new ConfigurationBuilder().AddUserSecrets(typeof(ApplicationDbContext).Assembly,optional:true).Build();
        var source=Environment.GetEnvironmentVariable("CREMS_TEST_SQL_CONNECTION")??config.GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(source));
        var name="CremsInitializationTest_"+Guid.NewGuid().ToString("N");
        var connection=new SqlConnectionStringBuilder(source){InitialCatalog=name}.ConnectionString;
        var content=Path.Combine(Path.GetTempPath(),name);Directory.CreateDirectory(content);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development",ContentRootPath=content});
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["BootstrapAdmin:Email"]="bootstrap@example.test",["BootstrapAdmin:Password"]="TestingOnly123!",["BootstrapAdmin:FullName"]="Fixture administrator"});
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(connection));
        builder.Services.AddIdentityApiEndpoints<ApplicationUser>().AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var app=builder.Build();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        try
        {
            await app.InitializeAsync(token);
            var assets=await db.Assets.CountAsync(token);var branches=await db.Branches.CountAsync(token);var divisions=await db.Divisions.CountAsync(token);
            Assert.True(assets>0);Assert.True(branches>0);Assert.True(divisions>0);
            Assert.Equal(SystemRoles.All.Length,await db.Roles.CountAsync(token));
            var admin=await db.Users.SingleAsync(x=>x.Email=="bootstrap@example.test",token);
            Assert.Equal("Fixture administrator",admin.FullName);Assert.True(admin.EmailConfirmed);Assert.True(admin.IsActive);
            Assert.True(await db.UserRoles.AnyAsync(x=>x.UserId==admin.Id,token));
            Assert.False(await db.Assets.AnyAsync(x=>!db.Branches.Any(b=>b.Id==x.BranchId),token));
            Assert.True(await db.BranchDivisionServices.AnyAsync(x=>x.IsActive&&x.IsBookable,token));
            Assert.True(await db.InspectionTemplates.AnyAsync(x=>x.IsActive,token));
            await app.InitializeAsync(token);
            Assert.Equal(assets,await db.Assets.CountAsync(token));Assert.Equal(branches,await db.Branches.CountAsync(token));Assert.Equal(divisions,await db.Divisions.CountAsync(token));
            Assert.Equal(1,await db.Users.CountAsync(x=>x.Email=="bootstrap@example.test",token));
            builder.Configuration["DevelopmentData:RefreshOnStartup"]="true";
            await app.InitializeAsync(token);
            Assert.Equal(assets,await db.Assets.CountAsync(token));Assert.Equal(branches,await db.Branches.CountAsync(token));
            Assert.False(await db.InAppNotifications.AnyAsync(token));
        }
        finally
        {
            var actual=new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
            if(actual!=name||!actual.StartsWith("CremsInitializationTest_"))throw new InvalidOperationException("Refusing cleanup outside the isolated fixture database.");
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
            Directory.Delete(content,true);
        }
    }
}
