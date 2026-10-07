using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
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

public sealed class MaintenanceSqlIntegrationTests
{
    public static bool SqlEnabled => Environment.GetEnvironmentVariable("CREMS_RUN_SQL_TESTS") == "1";

    [Fact(SkipUnless = nameof(SqlEnabled), Skip = "Set CREMS_RUN_SQL_TESTS=1 to run against an isolated SQL test database.")]
    public async Task Authenticated_maintenance_stock_release_reporting_and_concurrency_use_real_SQL()
    {
        var token = TestContext.Current.CancellationToken;
        var config = new ConfigurationBuilder().AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true).Build();
        var source = Environment.GetEnvironmentVariable("CREMS_TEST_SQL_CONNECTION") ?? config.GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(source));
        var database = "CremsMaintenanceTest_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(source) { InitialCatalog = database }.ConnectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await using var db = new ApplicationDbContext(options);
        WebApplication? app = null;
        try
        {
            await db.Database.MigrateAsync(token);
            var branch = new Branch { Code = "SQLTEST", Name = "SQL test branch" };
            var division = new Division { Code = "SQLTEST", Name = "SQL test division" };
            var user = new ApplicationUser { FullName = "SQL tester", UserName = "sql-tester", BranchId = branch.Id, DivisionId = division.Id };
            var asset = new Asset { AssetNumber = "SQL-1", Name = "SQL test asset", BranchId = branch.Id, DivisionId = division.Id };
            var stock = new InventoryPart { PartNumber = "SQL-PART", Name = "SQL test part", BranchId = branch.Id, QuantityOnHand = 1, UnitCost = 10 };
            db.AddRange(branch, division, user, asset, stock,
                new RolePermission { RoleName = SystemRoles.Administrator, Permission = SystemPermissions.AssetsView },
                new RolePermission { RoleName = SystemRoles.Administrator, Permission = SystemPermissions.AssetsViewFinancials },
                new RolePermission { RoleName = SystemRoles.Administrator, Permission = SystemPermissions.AssetsInspect },
                new RolePermission { RoleName = SystemRoles.Administrator, Permission = SystemPermissions.MaintenanceComplete });
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
            db.ChangeTracker.Clear();

            // This isolated Kestrel host uses the real controllers, cookie authentication, policies and SQL provider.
            // It intentionally excludes production startup seeding, email workers and the external login flow.
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
            builder.Services.AddScoped<CurrentStaffScope>();
            builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddApplicationAuthorization();
            builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme, o =>
            {
                o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
            });
            builder.Services.AddControllers().AddApplicationPart(typeof(MaintenanceJobsController).Assembly)
                .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddProblemDetails(); builder.Services.AddExceptionHandler<MaintenanceConflictHandler>();
            app = builder.Build(); app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            app.MapPost("/test-session", async (HttpContext context) =>
                await context.SignInAsync(IdentityConstants.ApplicationScheme, new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, SystemRoles.Administrator)
                ], IdentityConstants.ApplicationScheme))));
            await app.StartAsync(token);
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
                { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(60) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/maintenance-jobs", token)).StatusCode);
            (await client.PostAsync("/test-session", null, token)).EnsureSuccessStatusCode();
            var created = await client.PostAsJsonAsync("/api/maintenance-jobs", new SaveMaintenanceJobRequest(asset.Id, "Repair", "SQL repair", null, null, 0, null), token);
            created.EnsureSuccessStatusCode();
            var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("id").GetGuid();
            var jobs = await client.GetFromJsonAsync<JsonElement>("/api/maintenance-jobs", token);
            var version = jobs[0].GetProperty("version").GetDateTimeOffset();
            var workspace = await client.GetFromJsonAsync<JsonElement>("/api/maintenance-jobs/workspace?search=SQL%20repair&sort=asset&pageSize=25", token);
            Assert.Equal(1, workspace.GetProperty("total").GetInt32());
            Assert.Single(workspace.GetProperty("items").EnumerateArray());
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/maintenance-jobs/{jobId}/workspace", token);
            Assert.NotEmpty(detail.GetProperty("safetyChecks").EnumerateArray());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/maintenance-jobs/workspace?pageSize=24", token)).StatusCode);
            (await client.GetAsync("/api/maintenance-jobs/schedule?pageSize=25", token)).EnsureSuccessStatusCode();
            Assert.False(BookingPolicy.IsOperational(await db.Assets.AsNoTracking().SingleAsync(x => x.Id == asset.Id, token)));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/business-operations/assets/{asset.Id}/lifecycle",
                new LifecycleRequest(CREMS.Api.Domain.Operations.AssetLifecycleEventType.ReturnedToService, AssetStatus.Available, null, null, null, null), token)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/business-operations/maintenance/{jobId}/parts", new PartUsageRequest(stock.Id, 0.5m), token)).StatusCode);
            var issues = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(
                $"/api/business-operations/maintenance/{jobId}/parts", new PartUsageRequest(stock.Id, 1), token)));
            Assert.Single(issues, response => response.IsSuccessStatusCode);
            Assert.All(issues.Where(response => !response.IsSuccessStatusCode), response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
            Assert.Equal(0, (await db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == stock.Id, token)).QuantityOnHand);
            Assert.Equal(1, await db.MaintenancePartUsages.CountAsync(token));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/maintenance-jobs/{jobId}",
                MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with { ExpectedVersion = version }, token)).StatusCode);
            db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = user.Id, Permission = SystemPermissions.MaintenanceComplete, IsGranted = false });
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/maintenance-jobs/{jobId}", MaintenanceJobsTests.Request(MaintenanceStatus.Completed), token)).StatusCode);
            db.UserPermissionOverrides.RemoveRange(db.UserPermissionOverrides);
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
            (await client.PutAsJsonAsync($"/api/maintenance-jobs/{jobId}", MaintenanceJobsTests.Request(MaintenanceStatus.Completed), token)).EnsureSuccessStatusCode();
            Assert.False(BookingPolicy.IsOperational(await db.Assets.AsNoTracking().SingleAsync(x => x.Id == asset.Id, token)));
            var completed = await db.MaintenanceJobs.AsNoTracking().SingleAsync(x => x.Id == jobId, token);
            (await client.PostAsJsonAsync($"/api/maintenance-jobs/{jobId}/release", new ReleaseMaintenanceRequest(completed.UpdatedAt ?? completed.CreatedAt, "All safety checks passed", 100, MaintenanceWorkspace.Checks(asset, null)), token)).EnsureSuccessStatusCode();
            Assert.True(BookingPolicy.IsOperational(await db.Assets.AsNoTracking().SingleAsync(x => x.Id == asset.Id, token)));
            var report = await client.GetFromJsonAsync<JsonElement>("/api/business-operations/asset-profitability", token);
            Assert.Equal(30m, report.GetProperty("assets")[0].GetProperty("maintenanceCost").GetDecimal());

            var parts = await client.GetFromJsonAsync<JsonElement>($"/api/maintenance-jobs/{jobId}/parts", token);
            Assert.False(parts.GetProperty("canIssue").GetBoolean());
            var usageId = parts.GetProperty("ledger")[0].GetProperty("id").GetGuid();
            var returnDeny = new UserPermissionOverride { UserId = user.Id, Permission = SystemPermissions.MaintenanceComplete, IsGranted = false };
            db.UserPermissionOverrides.Add(returnDeny);
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/maintenance-jobs/{jobId}/parts/{usageId}/return",
                new ReturnMaintenancePartRequest(1, "Unused part"), token)).StatusCode);
            db.UserPermissionOverrides.Remove(returnDeny);
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
            var repricedStock = await db.InventoryParts.SingleAsync(x => x.Id == stock.Id, token);
            repricedStock.UnitCost = 25;
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/maintenance-jobs/{jobId}",
                MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with { PartsCost = 0 }, token)).StatusCode);
            var returns = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(
                $"/api/maintenance-jobs/{jobId}/parts/{usageId}/return", new ReturnMaintenancePartRequest(1, "Unused sealed part"), token)));
            Assert.Single(returns, response => response.IsSuccessStatusCode);
            Assert.All(returns.Where(response => !response.IsSuccessStatusCode), response => Assert.Contains(response.StatusCode,
                new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
            Assert.Equal(1, (await db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == stock.Id, token)).QuantityOnHand);
            Assert.Equal(0m, await db.MaintenancePartUsages.SumAsync(x => x.Quantity, token));
            Assert.Equal(10m, (await db.MaintenancePartUsages.SingleAsync(x => x.Quantity < 0, token)).UnitCost);
            Assert.Equal(20m, (await db.MaintenanceJobs.AsNoTracking().SingleAsync(x => x.Id == jobId, token)).ActualCost);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/maintenance-jobs/{jobId}/parts/{usageId}/return",
                new ReturnMaintenancePartRequest(1, "Duplicate return"), token)).StatusCode);

            (await client.PutAsJsonAsync($"/api/maintenance-jobs/{jobId}", MaintenanceJobsTests.Request(MaintenanceStatus.Completed) with {
                PartsCost = 100, LabourCost = 20, TaxMode = MaintenanceTaxMode.Inclusive, TaxRate = 12.5m,
                TaxableCosts = MaintenanceTaxableCosts.Parts | MaintenanceTaxableCosts.Labour, TaxCost = 999 }, token)).EnsureSuccessStatusCode();
            var taxSaved = await db.MaintenanceJobs.AsNoTracking().SingleAsync(x => x.Id == jobId, token);
            Assert.Equal(13.33m, taxSaved.TaxCost); Assert.Equal(120m, taxSaved.ActualCost);
            Assert.Equal(MaintenanceTaxMode.Inclusive, taxSaved.TaxMode);
            var taxResponse = await client.GetFromJsonAsync<JsonElement>("/api/maintenance-jobs", token);
            Assert.Equal("Inclusive", taxResponse[0].GetProperty("taxMode").GetString());
            Assert.Equal(12.5m, taxResponse[0].GetProperty("taxRate").GetDecimal());

            var inclusiveRequest = MaintenanceJobsTests.Request(MaintenanceStatus.InProgress) with {
                PartsCost = 100, LabourCost = 20, TaxMode = MaintenanceTaxMode.Inclusive, TaxRate = 12.5m,
                TaxableCosts = MaintenanceTaxableCosts.Parts | MaintenanceTaxableCosts.Labour, TransitionReason = "Reopen to verify tax and stock reconciliation" };
            (await client.PutAsJsonAsync($"/api/maintenance-jobs/{jobId}", inclusiveRequest, token)).EnsureSuccessStatusCode();
            var taxedIssue = await client.PostAsJsonAsync($"/api/business-operations/maintenance/{jobId}/parts", new PartUsageRequest(stock.Id, 1), token);
            taxedIssue.EnsureSuccessStatusCode();
            var taxedUsageId = (await taxedIssue.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("id").GetGuid();
            var afterIssue = await db.MaintenanceJobs.AsNoTracking().SingleAsync(x => x.Id == jobId, token);
            Assert.Equal(145m, afterIssue.ActualCost); Assert.Equal(13.33m, afterIssue.TaxCost);
            (await client.PostAsJsonAsync($"/api/maintenance-jobs/{jobId}/parts/{taxedUsageId}/return",
                new ReturnMaintenancePartRequest(1, "Unused part from inclusive-cost job"), token)).EnsureSuccessStatusCode();
            var afterReturn = await db.MaintenanceJobs.AsNoTracking().SingleAsync(x => x.Id == jobId, token);
            Assert.Equal(120m, afterReturn.ActualCost); Assert.Equal(13.33m, afterReturn.TaxCost);
            (await client.PutAsJsonAsync($"/api/maintenance-jobs/{jobId}", inclusiveRequest with { Status = MaintenanceStatus.Completed }, token)).EnsureSuccessStatusCode();

            (await client.PostAsJsonAsync($"/api/assets/{asset.Id}/inspections",
                new AssetInspectionInput(null, null, InspectionStage.Maintenance,
                    InspectionOutcome.Failed, null, null, null, null, null,
                    null, null, null, null, "Brake inspection failed"), token)).EnsureSuccessStatusCode();
            var repair = await db.MaintenanceJobs.AsNoTracking().SingleAsync(x => x.AssetId == asset.Id && x.Status == MaintenanceStatus.Open, token);
            Assert.Equal("Brake inspection failed", repair.FaultDescription);
            await using (var transferDb = new ApplicationDbContext(options))
            {
                var transferring = await transferDb.Assets.SingleAsync(x => x.Id == asset.Id, token);
                transferring.BranchId = Guid.NewGuid();
                await Assert.ThrowsAsync<MaintenanceConflictException>(() => transferDb.SaveChangesAsync(token));
            }
            (await client.PutAsJsonAsync($"/api/maintenance-jobs/{repair.Id}", MaintenanceJobsTests.Request(MaintenanceStatus.InProgress), token)).EnsureSuccessStatusCode();
            (await client.PutAsJsonAsync($"/api/maintenance-jobs/{repair.Id}", MaintenanceJobsTests.Request(MaintenanceStatus.Completed), token)).EnsureSuccessStatusCode();
            Assert.False(BookingPolicy.IsOperational(await db.Assets.AsNoTracking().SingleAsync(x => x.Id == asset.Id, token)));
            var completedRepair = await db.MaintenanceJobs.AsNoTracking().SingleAsync(x => x.Id == repair.Id, token);
            (await client.PostAsJsonAsync($"/api/maintenance-jobs/{repair.Id}/release", new ReleaseMaintenanceRequest(completedRepair.UpdatedAt ?? completedRepair.CreatedAt, "All safety checks passed", 100, MaintenanceWorkspace.Checks(asset, null)), token)).EnsureSuccessStatusCode();
            Assert.True(BookingPolicy.IsOperational(await db.Assets.AsNoTracking().SingleAsync(x => x.Id == asset.Id, token)));

            var due = await db.Assets.SingleAsync(x => x.Id == asset.Id, token);
            due.NextServiceDate = MaintenanceRules.LocalDate(DateTimeOffset.UtcNow);
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
            async Task Generate()
            {
                await using var workerDb = new ApplicationDbContext(options);
                try { await MaintenanceRules.GeneratePreventiveJobsAsync(workerDb, DateTimeOffset.UtcNow, token); }
                catch (Exception ex) when (MaintenanceConflictHandler.IsDeadlock(ex)) { /* The next worker cycle retries the rolled-back operation. */ }
            }
            await Task.WhenAll(Generate(), Generate());
            Assert.Equal(1, await db.MaintenanceJobs.CountAsync(x => x.AssetId == asset.Id && x.Status == MaintenanceStatus.Open, token));
            Assert.Single(await MaintenanceRules.DueAssets(db, db.Assets.AsNoTracking(), MaintenanceRules.LocalDate(DateTimeOffset.UtcNow)).ToListAsync(token));
        }
        finally
        {
            if (app is not null) { await app.StopAsync(CancellationToken.None); await app.DisposeAsync(); }
            // Never drop the source database: only the unique test database created by this invocation.
            if (new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog == database
                && database.StartsWith("CremsMaintenanceTest_", StringComparison.Ordinal))
                await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }
}
