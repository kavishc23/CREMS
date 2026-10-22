using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
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

public sealed class OrganizationSetupSqlIntegrationTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("CREMS_RUN_ORGANIZATION_SQL_TESTS") == "1";

    [Fact(SkipUnless = nameof(Enabled), Skip = "Enable CREMS_RUN_ORGANIZATION_SQL_TESTS for disposable SQL/HTTP verification.")]
    public async Task Setup_persists_through_HTTP_and_drives_scoped_fields_and_inspections()
    {
        var token = TestContext.Current.CancellationToken;
        var configuration = new ConfigurationBuilder().AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true).Build();
        var source = Environment.GetEnvironmentVariable("CREMS_TEST_SQL_CONNECTION") ?? configuration.GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(source));
        var database = "CremsOrganizationTest_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(source) { InitialCatalog = database }.ConnectionString;
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        WebApplication? app = null;
        try
        {
            await db.Database.MigrateAsync(token);
            var branch = new Branch { Code = "SUV", Name = "Setup test Suva" };
            var user = new ApplicationUser { UserName = "organization-test", FullName = "Organization test", BranchId = branch.Id };
            var customer = new Customer { CustomerNumber = "ORG-TEST", Name = "Test customer" };
            db.AddRange(branch, user, customer);
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
            builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
            builder.Services.AddScoped<CurrentStaffScope>();
            builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddScoped<IEmailQueue, EmailQueue>();
            builder.Services.AddApplicationAuthorization();
            builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme, o => {
                o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
            });
            builder.Services.AddControllers().AddApplicationPart(typeof(OrganizationSetupController).Assembly)
                .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
            app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            app.MapPost("/test-session/{role}", async (HttpContext context, string role) => await context.SignInAsync(IdentityConstants.ApplicationScheme,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, role)], IdentityConstants.ApplicationScheme))));
            await app.StartAsync(token);
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
            var check = "[{\"section\":\"Condition\",\"items\":[{\"label\":\"Hydraulics checked\"}]}]";
            var document = new DivisionSetupDocument(null, null, new SaveDivisionRequest("ORG", "Organization test", null, null, null, DivisionCapabilities.Rental | DivisionCapabilities.Maintenance, true),
                new DivisionBookingFlow(CustomerFields: [new("WORKSITE", "Worksite address", Required: true, ServiceCode: "HIRE")]),
                [new(null, new SaveServiceRequest("HIRE", "Equipment hire", null, ServiceOfferingType.EquipmentHire, PersonnelRequirement.None, true, false, InspectionRequirementsJson: "{\"tracksMeter\":true,\"tracksFuel\":false}"), [branch.Id],
                    [new(null, new CategoryRequest(Guid.Empty, null, "EXCAVATOR", "Excavators", null, "Engine hours", PersonnelRequirement.None, DefaultAssetType: AssetType.HeavyEquipment),
                        [new("SERIAL", "Serial number", AttributeDataType.Text, null, true, true, false, true, 0, "[]")],
                        [new(null, "Handover", InspectionStage.PreHire, check), new(null, "Return", InspectionStage.PostHire, check)])])]);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/organization/setup", document, OrganizationConfiguration.Json, token)).StatusCode);
            (await client.PostAsync("/test-session/" + SystemRoles.RentalOfficer, null, token)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/organization/setup", document, OrganizationConfiguration.Json, token)).StatusCode);
            (await client.PostAsync("/test-session/" + SystemRoles.SuperAdministrator, null, token)).EnsureSuccessStatusCode();
            var create = await client.PostAsJsonAsync("/api/organization/setup", document, OrganizationConfiguration.Json, token);
            Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync(token));
            var divisionId = (await create.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("id").GetGuid();
            var saved = await client.GetFromJsonAsync<DivisionSetupDocument>($"/api/organization/setup/{divisionId}", OrganizationConfiguration.Json, token);
            Assert.NotNull(saved);
            var serviceId = saved.Services[0].Id!.Value; var categoryId = saved.Services[0].Categories[0].Id!.Value;
            Assert.Equal(AssetType.HeavyEquipment, saved.Services[0].Categories[0].Category.DefaultAssetType);
            db.ChangeTracker.Clear();
            user = await db.Users.SingleAsync(token); user.DivisionId = divisionId;
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);

            var assetResponse = await client.PostAsJsonAsync("/api/assets", new { assetNumber = "ORG-EX-1", name = "Test excavator", type = "HeavyEquipment", status = "Available", divisionId, branchId = branch.Id,
                serviceOfferingId = serviceId, assetCategoryId = categoryId, dailyRate = 100, inheritBond = true, categoryAttributes = new Dictionary<string, string> { ["SERIAL"] = "TEST-SN-1" } }, token);
            Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);
            var assetId = (await assetResponse.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("id").GetGuid();
            Assert.Equal("TEST-SN-1", (await db.AssetAttributeValues.AsNoTracking().SingleAsync(token)).Value);
            var first = saved.Services[0];
            var removal = saved with { Services = [first with { Categories = [first.Categories[0] with { Attributes = [] }] }] };
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/organization/setup", removal, OrganizationConfiguration.Json, token)).StatusCode);
            Assert.Single(await db.AssetAttributeDefinitions.AsNoTracking().ToListAsync(token));

            var periods = Enum.GetValues<DayOfWeek>().Select(day => new OperatingPeriodRequest(day, new(8, 0), new(17, 0), new(16, 30), new(16, 30), false, 0)).ToArray();
            var branchState = await client.GetFromJsonAsync<JsonElement>($"/api/branches/{branch.Id}/configuration", token);
            var invalidOperations = new SaveBranchOperatingConfiguration(periods, [], [new(divisionId, Guid.NewGuid(), true, true)], branchState.GetProperty("revision").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/branches/{branch.Id}/operating-configuration", invalidOperations, OrganizationConfiguration.Json, token)).StatusCode);
            Assert.Empty(await db.BranchOperatingPeriods.AsNoTracking().ToListAsync(token));
            var validOperations = invalidOperations with { Services = [new(divisionId, serviceId, true, true)] };
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/branches/{branch.Id}/operating-configuration", validOperations, OrganizationConfiguration.Json, token)).StatusCode);
            Assert.Equal(7, await db.BranchOperatingPeriods.CountAsync(token));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/branches/{branch.Id}/operating-configuration", validOperations, OrganizationConfiguration.Json, token)).StatusCode);
            // A branch edit makes the previously opened wizard stale rather than silently overwriting it.
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/organization/setup", saved, OrganizationConfiguration.Json, token)).StatusCode);

            var asset = await db.Assets.Include(x => x.Division).Include(x => x.ServiceOffering).Include(x => x.AssetCategory).SingleAsync(x => x.Id == assetId, token);
            var snapshot = await ConfiguredHireRules.Capture(db, asset, new Dictionary<string, string> { ["WORKSITE"] = "Suva" }, token);
            var booking = new Booking { BookingNumber = "ORG-BK-1", BranchId = branch.Id, CustomerId = customer.Id, Status = BookingStatus.Confirmed,
                ConfigurationSnapshotJson = JsonSerializer.Serialize(snapshot, OrganizationConfiguration.Json), Items = [new BookingItem { AssetId = asset.Id, StartAt = DateTimeOffset.UtcNow.AddDays(1), EndAt = DateTimeOffset.UtcNow.AddDays(2), DailyRate = 100 }] };
            db.Bookings.Add(booking);
            var template = await db.InspectionTemplates.SingleAsync(x => x.AssetCategoryId == categoryId && x.Stage == InspectionStage.PreHire, token);
            template.ChecklistJson = check.Replace("Hydraulics checked", "Changed later");
            using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
            (await client.PostAsync("/test-session/" + SystemRoles.RentalOfficer, null, token)).EnsureSuccessStatusCode();
            var context = await client.GetFromJsonAsync<JsonElement>($"/api/rentals/{booking.Id}/inspection-context", token);
            Assert.Contains("Hydraulics checked", context.GetProperty("templates")[0].GetProperty("checklistJson").GetString());
            Assert.False(context.GetProperty("tracksFuel").GetBoolean());
            var wrongCheck = new { scannedAssetNumber = asset.AssetNumber, conditionNotes = "Checked", meterReading = 100, evidenceDataUrls = new[] { "data:image/jpeg;base64,AAAA" }, checklistItems = new[] { "Invented check" } };
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/rentals/{booking.Id}/pre-hire-inspection", wrongCheck, token)).StatusCode);
            var correctCheck = new { wrongCheck.scannedAssetNumber, wrongCheck.conditionNotes, wrongCheck.meterReading, wrongCheck.evidenceDataUrls, checklistItems = new[] { "Condition: Hydraulics checked" } };
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/rentals/{booking.Id}/pre-hire-inspection", correctCheck, token)).StatusCode);
            Assert.Single(await db.RentalInspections.AsNoTracking().ToListAsync(token));
            var staffConfiguration = await client.GetFromJsonAsync<JsonElement>("/api/organization/operating-context", token);
            Assert.Equal(1, staffConfiguration.GetArrayLength());
            Assert.Equal(branch.Id, staffConfiguration[0].GetProperty("branchId").GetGuid());
            Assert.Equal(7, staffConfiguration[0].GetProperty("periods").GetArrayLength());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/organization/setup/{divisionId}", token)).StatusCode);
        }
        finally
        {
            if (app is not null) { await app.StopAsync(CancellationToken.None); await app.DisposeAsync(); }
            if (new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog != database || !database.StartsWith("CremsOrganizationTest_", StringComparison.Ordinal)) throw new InvalidOperationException("Refusing to remove a non-test database.");
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }
}
