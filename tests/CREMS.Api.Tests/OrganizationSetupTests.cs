using System.Security.Claims;
using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class OrganizationSetupTests
{
    private static readonly CancellationToken Token = CancellationToken.None;

    [Fact]
    public async Task Guided_setup_persists_the_complete_division_and_stable_related_records()
    {
        await using var f = await Fixture.Create();
        var request = f.NewSetup();
        var response = Assert.IsType<OkObjectResult>(await f.Controller.Save(request, Token));
        var division = await f.Db.Divisions.SingleAsync(Token);
        var saved = Assert.IsType<DivisionSetupDocument>(Assert.IsType<OkObjectResult>(await f.Controller.Get(division.Id, Token)).Value);
        Assert.NotNull(response.Value);
        Assert.Single(saved.Services);
        Assert.Single(saved.Services[0].Categories);
        Assert.Equal(2, saved.Services[0].Categories[0].Templates.Count);
        Assert.Single(await f.Db.BranchDivisions.ToListAsync(Token));
        Assert.Single(await f.Db.BranchDivisionServices.ToListAsync(Token));
        Assert.Equal("WORKSITE", DivisionBookingFlow.For(division).CustomerFields![0].Code);

        var fieldId = (await f.Db.AssetAttributeDefinitions.SingleAsync(Token)).Id;
        Assert.IsType<OkObjectResult>(await f.Controller.Save(saved with { Division = saved.Division with { Description = "Updated description" } }, Token));
        Assert.Equal(fieldId, (await f.Db.AssetAttributeDefinitions.SingleAsync(Token)).Id);
        Assert.Equal(2, await f.Db.InspectionTemplates.CountAsync(Token));
    }

    [Fact]
    public async Task Invalid_later_service_leaves_no_partial_setup_saved()
    {
        await using var f = await Fixture.Create();
        var request = f.NewSetup();
        var bad = request.Services[0] with { Service = request.Services[0].Service with { Code = "SECOND" }, Categories = [request.Services[0].Categories[0] with { Category = request.Services[0].Categories[0].Category with { Code = "SECOND" }, Templates = [new(null, "Broken", InspectionStage.PreHire, "[]")] }] };
        Assert.IsType<BadRequestObjectResult>(await f.Controller.Save(request with { Services = [request.Services[0], bad] }, Token));
        Assert.Empty(await f.Db.Divisions.AsNoTracking().ToListAsync(Token));
        Assert.Empty(await f.Db.ServiceOfferings.AsNoTracking().ToListAsync(Token));
        Assert.Empty(await f.Db.AssetCategories.AsNoTracking().ToListAsync(Token));
    }

    [Fact]
    public async Task Stale_setup_does_not_overwrite_changes_from_an_existing_editor()
    {
        await using var f = await Fixture.Create();
        await f.Controller.Save(f.NewSetup(), Token);
        var division = await f.Db.Divisions.SingleAsync(Token);
        var saved = Assert.IsType<DivisionSetupDocument>(Assert.IsType<OkObjectResult>(await f.Controller.Get(division.Id, Token)).Value);
        var service = await f.Db.ServiceOfferings.SingleAsync(Token);
        service.Description = "Changed by another administrator";
        using (f.Db.SuppressNotifications()) await f.Db.SaveChangesAsync(Token);
        Assert.IsType<ConflictObjectResult>(await f.Controller.Save(saved, Token));
        Assert.Equal("Changed by another administrator", (await f.Db.ServiceOfferings.SingleAsync(Token)).Description);
    }

    [Fact]
    public async Task Both_editors_protect_asset_values_when_removing_or_retyping_fields()
    {
        await using var f = await Fixture.Create();
        await f.Controller.Save(f.NewSetup(), Token);
        var division = await f.Db.Divisions.SingleAsync(Token);
        var category = await f.Db.AssetCategories.SingleAsync(Token);
        var definition = await f.Db.AssetAttributeDefinitions.SingleAsync(Token);
        var service = await f.Db.ServiceOfferings.SingleAsync(Token);
        var asset = new Asset { AssetNumber = "EX-1", Name = "Excavator", Type = AssetType.HeavyEquipment, BranchId = f.Branch.Id, DivisionId = division.Id, ServiceOfferingId = service.Id, AssetCategoryId = category.Id };
        f.Db.AddRange(asset, new AssetAttributeValue { AssetId = asset.Id, AttributeDefinitionId = definition.Id, Value = "SN-123" });
        using (f.Db.SuppressNotifications()) await f.Db.SaveChangesAsync(Token);
        var saved = Assert.IsType<DivisionSetupDocument>(Assert.IsType<OkObjectResult>(await f.Controller.Get(division.Id, Token)).Value);
        var first = saved.Services[0];
        Assert.IsType<ConflictObjectResult>(await f.Controller.Save(saved with { Services = [first with { Categories = [first.Categories[0] with { Attributes = [] }] }] }, Token));
        var controller = new AssetCategoriesController(f.Db, new CurrentStaffScope(f.Db)) { ControllerContext = f.Controller.ControllerContext };
        Assert.IsType<ConflictObjectResult>(await controller.Attributes(category.Id, [], Token));
        Assert.Equal("SN-123", (await f.Db.AssetAttributeValues.SingleAsync(Token)).Value);
        Assert.Equal(definition.Id, (await f.Db.AssetAttributeDefinitions.SingleAsync(Token)).Id);
    }

    [Fact]
    public void Customer_questions_apply_to_their_service_and_category_and_validate_types()
    {
        var flow = new DivisionBookingFlow(CustomerFields: [new("SITE", "Worksite", Required: true, ServiceCode: "HEAVY"), new("ROOMS", "Rooms", "Integer", CategoryCode: "SPACE"), new("ACCESS", "Site accessible", "Boolean", true)]);
        var service = new ServiceOffering { Code = "HEAVY", Name = "Heavy hire" };
        var category = new AssetCategory { Code = "EXCAVATOR", Name = "Excavators" };
        var questions = OrganizationConfiguration.Questions(flow, service, category);
        Assert.Equal(2, questions.Count);
        Assert.Equal("Worksite is required.", OrganizationConfiguration.ValidateAnswers(questions, new Dictionary<string, string> { ["ACCESS"] = "false" }));
        Assert.Null(OrganizationConfiguration.ValidateAnswers(questions, new Dictionary<string, string> { ["SITE"] = "Walu Bay", ["ACCESS"] = "false" }));
        Assert.NotNull(OrganizationConfiguration.ValidateAnswers(questions, new Dictionary<string, string> { ["SITE"] = "Walu Bay", ["ACCESS"] = "maybe" }));
        Assert.NotNull(OrganizationConfiguration.ValidateAnswers([new("ROOMS", "Rooms", "Integer", true)], new Dictionary<string, string> { ["ROOMS"] = "2.5" }));
    }

    [Fact]
    public async Task Captured_inspections_survive_later_template_changes_and_enforce_real_checks()
    {
        await using var f = await Fixture.Create();
        await f.Controller.Save(f.NewSetup(), Token);
        var division = await f.Db.Divisions.SingleAsync(Token);
        var category = await f.Db.AssetCategories.SingleAsync(Token);
        var service = await f.Db.ServiceOfferings.SingleAsync(Token);
        var asset = new Asset { AssetNumber = "EX-2", Name = "Excavator", Type = AssetType.HeavyEquipment, BranchId = f.Branch.Id, DivisionId = division.Id, Division = division, ServiceOfferingId = service.Id, ServiceOffering = service, AssetCategoryId = category.Id, AssetCategory = category };
        var booking = new Booking { BookingNumber = "BK-SNAPSHOT", BranchId = f.Branch.Id, Items = [new BookingItem { AssetId = asset.Id, Asset = asset }] };
        var snapshot = await ConfiguredHireRules.Capture(f.Db, asset, new Dictionary<string, string> { ["WORKSITE"] = "Suva" }, Token);
        booking.ConfigurationSnapshotJson = JsonSerializer.Serialize(snapshot, OrganizationConfiguration.Json);
        var template = await f.Db.InspectionTemplates.FirstAsync(x => x.Stage == InspectionStage.PreHire, Token);
        template.ChecklistJson = Checklist("New check");
        using (f.Db.SuppressNotifications()) await f.Db.SaveChangesAsync(Token);
        Assert.NotNull(await ConfiguredHireRules.ValidateInspection(f.Db, booking, InspectionStage.PreHire, ["Condition: New check"], [], 100, 75, false, Token));
        Assert.Null(await ConfiguredHireRules.ValidateInspection(f.Db, booking, InspectionStage.PreHire, ["Condition: Hydraulics checked"], [], 100, 75, false, Token));
        Assert.NotNull(await ConfiguredHireRules.ValidateInspection(f.Db, booking, InspectionStage.PreHire, [], ["Condition: Hydraulics checked"], 100, 75, false, Token));
        Assert.Null(await ConfiguredHireRules.ValidateInspection(f.Db, booking, InspectionStage.PreHire, [], ["Condition: Hydraulics checked"], 100, 75, true, Token));
        Assert.NotNull(await ConfiguredHireRules.ValidateInspection(f.Db, booking, InspectionStage.PreHire, ["Condition: Hydraulics checked"], [], null, 75, false, Token));
    }

    [Fact]
    public void Calendar_maintenance_uses_the_latest_service_baseline_without_changing_explicit_targets()
    {
        var today = new DateOnly(2026, 10, 22);
        var asset = new Asset { AssetNumber = "BIN-1", Name = "Site bin", CreatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero) };
        Assert.True(MaintenanceRules.Plan(asset, null, "{\"serviceIntervalDays\":30}", today).IsDue);
        var completed = new MaintenanceJob { JobNumber = "MNT-1", ServiceType = "Inspection", Status = MaintenanceStatus.Completed, CompletedAt = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero) };
        Assert.False(MaintenanceRules.Plan(asset, completed, "{\"serviceIntervalDays\":30}", today).IsDue);
        asset.NextServiceDate = today.AddDays(10);
        Assert.Equal(asset.NextServiceDate, MaintenanceRules.Plan(asset, null, "{\"serviceIntervalDays\":30}", today).NextServiceDate);
    }

    [Fact]
    public async Task Manual_quote_conversion_requires_customer_answers_and_captures_the_same_templates()
    {
        await using var f = await Fixture.Create();
        await f.Controller.Save(f.NewSetup(), Token);
        var division = await f.Db.Divisions.SingleAsync(Token);
        var service = await f.Db.ServiceOfferings.SingleAsync(Token);
        var category = await f.Db.AssetCategories.SingleAsync(Token);
        var entity = new CREMS.Api.Domain.Customers.Customer { CustomerNumber = "MANUAL", Name = "Manual customer" };
        var asset = new Asset { AssetNumber = "MANUAL-ASSET", Name = "Excavator", Type = AssetType.HeavyEquipment, BranchId = f.Branch.Id, DivisionId = division.Id, ServiceOfferingId = service.Id, AssetCategoryId = category.Id, DailyRate = 100 };
        var quote = new CREMS.Api.Domain.Corporate.SalesQuote { QuoteNumber = "QT-MANUAL", CustomerId = entity.Id, BranchId = f.Branch.Id, DivisionId = division.Id, Status = CREMS.Api.Domain.Corporate.QuoteStatus.Accepted, ValidUntil = DateTimeOffset.UtcNow.AddDays(14), Subtotal = 100, Total = 100,
            LineItemsJson = "[{\"description\":\"Hire\",\"quantity\":1,\"rate\":100,\"category\":\"BaseHire\",\"unit\":\"Day\"}]" };
        f.Db.AddRange(entity, asset, quote);
        using (f.Db.SuppressNotifications()) await f.Db.SaveChangesAsync(Token);
        var controller = new CorporateOperationsController(f.Db, new CurrentStaffScope(f.Db), new EmailQueue(), new AllowAuthorization()) { ControllerContext = f.Controller.ControllerContext };
        var request = new ConvertQuoteRequest(asset.Id, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2), 100, 0, null);
        Assert.IsType<BadRequestObjectResult>(await controller.ConvertQuote(quote.Id, request, Token));
        Assert.Empty(await f.Db.Bookings.ToListAsync(Token));
        Assert.IsType<OkObjectResult>(await controller.ConvertQuote(quote.Id, request with { ConfigurationAnswers = new Dictionary<string, string> { ["WORKSITE"] = "Walu Bay" } }, Token));
        var snapshot = OrganizationConfiguration.Read<BookingConfigurationSnapshot>((await f.Db.Bookings.SingleAsync(Token)).ConfigurationSnapshotJson);
        Assert.NotNull(snapshot);
        Assert.Equal("Walu Bay", snapshot.Answers["WORKSITE"]);
        Assert.Equal(2, snapshot.Templates.Count);
    }

    [Theory]
    [InlineData(SystemRoles.BranchManager)]
    [InlineData(SystemRoles.RentalOfficer)]
    [InlineData(SystemRoles.MaintenanceOfficer)]
    public async Task Branch_staff_receive_only_their_division_and_branch_configuration(string role)
    {
        await using var f = await Fixture.Create();
        await f.Controller.Save(f.NewSetup(), Token);
        var division = await f.Db.Divisions.SingleAsync(Token);
        var otherBranch = new Branch { Code = "OTHER", Name = "Other branch" };
        var otherDivision = new Division { Code = "OTHER", Name = "Other division" };
        var staff = new ApplicationUser { UserName = role, FullName = role, DivisionId = division.Id, BranchId = f.Branch.Id };
        f.Db.AddRange(otherBranch, otherDivision, staff, new BranchDivision { BranchId = otherBranch.Id, DivisionId = otherDivision.Id });
        using (f.Db.SuppressNotifications()) await f.Db.SaveChangesAsync(Token);
        var controller = new OperatingConfigurationController(f.Db, new CurrentStaffScope(f.Db)) { ControllerContext = Context(staff, role) };
        var result = Assert.IsType<OkObjectResult>(await controller.Get(Token));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, OrganizationConfiguration.Json));
        Assert.Equal(1, json.RootElement.GetArrayLength());
        Assert.Equal(f.Branch.Id, json.RootElement[0].GetProperty("branchId").GetGuid());
        Assert.Equal(division.Id, json.RootElement[0].GetProperty("divisionId").GetGuid());
    }


    [Fact]
    public async Task Branch_creation_saves_selected_services_atomically_and_rejects_unrelated_services()
    {
        await using var f = await Fixture.Create();
        await f.Controller.Save(f.NewSetup(), Token);
        var division = await f.Db.Divisions.SingleAsync(Token);
        var service = await f.Db.ServiceOfferings.SingleAsync(Token);
        var admin = await f.Db.Users.SingleAsync(Token);
        var controller = new BranchesController(f.Db, new CurrentStaffScope(f.Db)) { ControllerContext = Context(admin, SystemRoles.SuperAdministrator) };
        var request = new SaveBranchRequest("NEW", "New location", null, null, [division.Id], ServiceOfferingIds: [service.Id]);
        Assert.IsType<CreatedAtActionResult>((await controller.Create(request, Token)).Result);
        var branch = await f.Db.Branches.SingleAsync(x => x.Code == "NEW", Token);
        var link = await f.Db.BranchDivisionServices.SingleAsync(x => x.BranchId == branch.Id, Token);
        Assert.Equal(service.Id, link.ServiceOfferingId);
        Assert.True(link.IsActive);
        Assert.True(link.IsBookable);
        Assert.IsType<BadRequestObjectResult>((await controller.Create(request with { Code = "BAD", ServiceOfferingIds = [Guid.NewGuid()] }, Token)).Result);
        Assert.False(await f.Db.Branches.AnyAsync(x => x.Code == "BAD", Token));
        Assert.IsType<OkObjectResult>((await controller.Update(branch.Id, request with { ServiceOfferingIds = [] }, Token)).Result);
        Assert.False(link.IsActive);
        Assert.False(link.IsBookable);
    }

    private static string Checklist(string check) => JsonSerializer.Serialize(new[] { new { section = "Condition", items = new[] { new { label = check } } } });
    private static ControllerContext Context(ApplicationUser user, string role) => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, role)], "test")) } };
    private sealed class AllowAuthorization : Microsoft.AspNetCore.Authorization.IAuthorizationService
    {
        public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<Microsoft.AspNetCore.Authorization.IAuthorizationRequirement> requirements) => Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());
        public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public required ApplicationDbContext Db { get; init; }
        public required OrganizationSetupController Controller { get; init; }
        public required Branch Branch { get; init; }
        public static async Task<Fixture> Create()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"organization-{Guid.NewGuid():N}").Options);
            var admin = new ApplicationUser { UserName = "setup-admin", FullName = "Setup admin" };
            var branch = new Branch { Code = "SUV", Name = "Suva" };
            db.AddRange(admin, branch); using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
            return new() { Db = db, Branch = branch, Controller = new OrganizationSetupController(db, new CurrentStaffScope(db)) { ControllerContext = Context(admin, SystemRoles.SuperAdministrator) } };
        }
        public DivisionSetupDocument NewSetup() => new(null, null, new SaveDivisionRequest("TEST", "Carpenters test", "Equipment management", null, null, DivisionCapabilities.Rental | DivisionCapabilities.Maintenance, true),
            new DivisionBookingFlow(CustomerFields: [new("WORKSITE", "Worksite", Required: true, ServiceCode: "HEAVY")]),
            [new(null, new SaveServiceRequest("HEAVY", "Heavy equipment hire", null, ServiceOfferingType.EquipmentHire, PersonnelRequirement.None, true, false, InspectionRequirementsJson: "{\"tracksMeter\":true,\"tracksFuel\":true}"), [Branch.Id],
                [new(null, new CategoryRequest(Guid.Empty, null, "EXCAVATOR", "Excavators", null, "Engine hours", PersonnelRequirement.None),
                    [new("SERIAL", "Serial number", AttributeDataType.Text, null, true, true, false, true, 0, "[]")],
                    [new(null, "Handover", InspectionStage.PreHire, Checklist("Hydraulics checked")), new(null, "Return", InspectionStage.PostHire, Checklist("Condition compared"))])])]);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
