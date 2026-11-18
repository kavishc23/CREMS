using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Data;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CREMS.Api.Tests;

public sealed class SharedIncidentReportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Photo = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jO1cAAAAASUVORK5CYII=";
    private static async Task<(RentalLifecycleController Controller, Booking Booking, Asset Asset, RentalIncident Incident)> Setup(ApplicationDbContext db)
    {
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        asset.Type = AssetType.PassengerVehicle; asset.Status = AssetStatus.Rented; asset.CurrentMeterReading = 100000; asset.MeterUnit = "km";
        var customer = new Customer { CustomerNumber = "SHARED-CUSTOMER", Name = "Shared customer" };
        var booking = new Booking { BookingNumber = "SHARED-HIRE", BranchId = asset.BranchId, CustomerId = customer.Id,
            Status = BookingStatus.ConvertedToRental, DepositRequired = 100, BondAmountHeld = 100, BondStatus = BondStatus.Held,
            Items = [new BookingItem { AssetId = asset.Id, StartAt = DateTimeOffset.UtcNow.AddDays(-1), EndAt = DateTimeOffset.UtcNow.AddDays(2), DailyRate = 100, HireQuantity = 3 }],
            Inspections = [new RentalInspection { Type = InspectionType.Handover, MeterReading = 100000, FuelLevelPercent = 100,
                CompletedAt = DateTimeOffset.UtcNow.AddDays(-1), CompletedByName = "Original agent", ConditionNotes = "Original signed inspection" }] };
        var incident = new RentalIncident { BookingId = booking.Id, AssetId = asset.Id, IncidentNumber = "CUSTOMER-REPORT",
            Type = IncidentType.Damage, Description = "The customer reports a body scratch.", Location = "Suva", OccurredAt = DateTimeOffset.UtcNow.AddHours(-1), EvidenceJson = JsonSerializer.Serialize(new[] { Photo }) };
        db.AddRange(customer, booking, incident);
        db.AuthorizedDrivers.Add(new AuthorizedDriver { BookingId = booking.Id, FullName = "Customer", LicenceNumber = "12345678", LicenceClass = "2", LicenceExpiry = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), Verified = true });
        db.AssetLifecycleEvents.Add(new AssetLifecycleEvent { AssetId = asset.Id, BookingId = booking.Id, Type = AssetLifecycleEventType.CheckedOut,
            ToStatus = AssetStatus.Rented, OccurredAt = booking.Items.Single().StartAt, RecordedByName = "Original agent" });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        await SharedRentalIncidents.SyncCase(db, booking, incident, Token);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        return (new RentalLifecycleController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext }, booking, asset, incident);
    }
    private static StaffIncidentReport Report(Asset asset, RentalIncidentOutcome outcome = RentalIncidentOutcome.ContinueRental) => new(
        ExpectedAssetId: asset.Id, ExpectedReviewedAt: null, Outcome: outcome, Type: IncidentType.Damage,
        OccurredAt: DateTimeOffset.UtcNow.AddMinutes(-30), Description: "Staff report of the vehicle damage.", Location: "Suva depot",
        PoliceReference: "POL-REFERENCE", InsuranceClaimNumber: null, EstimatedCost: 30, EvidenceDataUrls: [Photo],
        Checks: new Dictionary<string,string> { ["body"] = "Issue", ["brakes"] = "Passed", ["safety"] = "Passed", ["operation"] = "Passed" },
        MeterReading: 100050, FuelPercent: 75, Assessment: "Minor body scratch only; safety checks passed and customer arrangement recorded.",
        SafeToContinue: true, Confirmed: true, AssetStatus: AssetStatus.Maintenance, Custody: "Suva recovery depot");

    [Fact]
    public async Task Customer_submission_staff_assessment_and_customer_summary_share_one_report_and_evidence()
    {
        await using var db = MaintenanceJobsTests.Database(); var (staff, booking, asset, _) = await Setup(db);
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(db);
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>();
        using var provider = services.BuildServiceProvider();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var customerUser = new ApplicationUser { UserName = "incident@example.test", CustomerId = booking.CustomerId };
        Assert.True((await users.CreateAsync(customerUser)).Succeeded);
        var customer = new CustomerAccountController(db, users,
            provider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>(), new EmailQueue(), null!)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, customerUser.Id.ToString())], "customer")) } } };
        Assert.IsType<AcceptedResult>(await customer.ReportIncident(booking.Id,
            new CustomerIncidentRequest(IncidentType.Breakdown, DateTimeOffset.UtcNow.AddMinutes(-20),
                "Customer reported a temporary warning light.", "Suva", "POL-CUSTOMER", [Photo]), Token));
        var report = await db.RentalIncidents.SingleAsync(i=>i.Type==IncidentType.Breakdown, Token);
        var count = await db.RentalIncidents.CountAsync(Token); var cases = await db.CustomerCases.CountAsync(Token);
        Assert.IsType<OkObjectResult>(await staff.SaveIncidentReport(booking.Id, report.Id, Report(asset), Token));
        Assert.Equal(count, await db.RentalIncidents.CountAsync(Token)); Assert.Equal(cases, await db.CustomerCases.CountAsync(Token));
        Assert.Equal("Customer reported a temporary warning light.", report.Description);
        var details = Assert.IsType<OkObjectResult>(await customer.BookingDetails(booking.Id, Token));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(details.Value));
        var shared = json.RootElement.GetProperty("Incidents").EnumerateArray().Single(i=>i.GetProperty("IncidentNumber").GetString()==report.IncidentNumber);
        Assert.Contains("normal return", shared.GetProperty("CustomerResolution").GetString());
        Assert.Contains(Photo, JsonSerializer.Deserialize<string[]>(shared.GetProperty("EvidenceJson").GetString()!)!);
        Assert.False(shared.TryGetProperty("StaffResolution", out _));
    }

    [Fact]
    public async Task Minor_issue_updates_the_customer_incident_and_case_without_ending_or_charging_the_hire()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, booking, asset, incident) = await Setup(db);
        var original = incident.Description; var total = RentalFinancials.Total(booking);
        Assert.IsType<OkObjectResult>(await controller.SaveIncidentReport(booking.Id, incident.Id, Report(asset), Token));
        Assert.Equal(BookingStatus.ConvertedToRental, booking.Status); Assert.Null(booking.IncidentClosedAt);
        Assert.Equal(AssetStatus.Rented, asset.Status); Assert.Equal(100, booking.BondAmountHeld);
        Assert.Equal(total, RentalFinancials.Total(booking)); Assert.Equal(original, incident.Description);
        Assert.Equal(RentalIncidentOutcome.ContinueRental, incident.Outcome); Assert.Contains("normal return", incident.CustomerResolution);
        Assert.Single(await db.RentalIncidents.ToListAsync(Token));
        Assert.Equal(incident.CustomerResolution, (await db.CustomerCases.SingleAsync(Token)).Resolution);
        Assert.Empty(await db.MaintenanceJobs.ToListAsync(Token)); Assert.Empty(await db.RentalPayments.ToListAsync(Token));
        Assert.IsType<ConflictObjectResult>(await controller.SaveIncidentReport(booking.Id, incident.Id, Report(asset), Token));
    }

    [Theory]
    [InlineData("unsafe")]
    [InlineData("unchecked")]
    [InlineData("missing-evidence")]
    [InlineData("bad-upload")]
    [InlineData("future")]
    [InlineData("decreasing-meter")]
    [InlineData("wrong-asset")]
    public async Task Invalid_or_stale_assessments_do_not_change_the_hire(string invalid)
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, booking, asset, incident) = await Setup(db);
        var request = Report(asset);
        request = invalid switch {
            "unsafe" => request with { Checks = new Dictionary<string,string>(request.Checks) { ["brakes"] = "Issue" } },
            "unchecked" => request with { Checks = new Dictionary<string,string>() },
            "missing-evidence" => request with { EvidenceDataUrls = [] },
            "bad-upload" => request with { EvidenceDataUrls = [Photo, "data:application/pdf;base64,aW52YWxpZA=="] },
            "future" => request with { OccurredAt = DateTimeOffset.UtcNow.AddHours(1) },
            "decreasing-meter" => request with { MeterReading = 99999 },
            _ => request with { ExpectedAssetId = Guid.NewGuid() }
        };
        var result = await controller.SaveIncidentReport(booking.Id, incident.Id, request, Token);
        Assert.True(result is BadRequestObjectResult or ConflictObjectResult);
        Assert.Null(incident.Outcome); Assert.Equal(BookingStatus.ConvertedToRental, booking.Status);
        Assert.Empty(await db.MaintenanceJobs.ToListAsync(Token));
    }

    [Fact]
    public async Task Replacement_preserves_money_and_original_signature_but_uses_the_new_assets_return_baseline()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, booking, asset, incident) = await Setup(db);
        var replacement = new Asset { AssetNumber = "REPLACE-1", Name = "Replacement vehicle", BranchId = asset.BranchId,
            Type = asset.Type, CurrentMeterReading = 1000, MeterUnit = "km", Status = AssetStatus.Available };
        var agreement = new RentalAgreement { BookingId = booking.Id, AgreementNumber = "ORIGINAL-AGREEMENT", BranchId = booking.BranchId,
            TermsVersion = "1", TermsJson = "[]", CustomerSnapshotJson = "{}", AssetSnapshotJson = "original immutable asset snapshot",
            PricingSnapshotJson = "original immutable pricing", CustomerSignatureName = "Customer", CustomerSignatureDataUrl = Photo,
            ApprovedByName = "Original agent", Status = AgreementStatus.Active };
        db.AddRange(replacement, agreement); using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var total = RentalFinancials.Total(booking); var end = booking.Items.Single().EndAt;
        var request = Report(asset, RentalIncidentOutcome.ReplaceAsset) with { ReplacementAssetId = replacement.Id,
            ReplacementHandover = new(["Identity and accessories checked", "Controls and safety checked", "Condition checked"], 1005, 100,
                [Photo], true, true, "Customer", Photo) };
        Assert.IsType<OkObjectResult>(await controller.SaveIncidentReport(booking.Id, incident.Id, request, Token));
        Assert.Equal(replacement.Id, booking.Items.Single().AssetId); Assert.Equal(AssetStatus.Rented, replacement.Status);
        Assert.Equal(AssetStatus.Maintenance, asset.Status); Assert.Equal(BookingStatus.ConvertedToRental, booking.Status);
        Assert.Null(booking.IncidentClosedAt); Assert.Equal(end, booking.Items.Single().EndAt);
        Assert.Equal(total, RentalFinancials.Total(booking)); Assert.Equal(100, booking.BondAmountHeld); Assert.Null(booking.BondSettledAt);
        Assert.Equal("original immutable asset snapshot", agreement.AssetSnapshotJson);
        Assert.Equal("original immutable pricing", agreement.PricingSnapshotJson); Assert.Equal(Photo, agreement.CustomerSignatureDataUrl);
        Assert.Single(await db.RentalAgreementAddendums.ToListAsync(Token)); Assert.Single(await db.MaintenanceJobs.ToListAsync(Token));
        var handover = await SharedRentalIncidents.CurrentHandover(db, booking, Token);
        Assert.Equal(1005, handover!.MeterReading); Assert.Equal(100000, booking.Inspections.Single().MeterReading);
        var usage = await AssetUsageSummary.Build(db, asset, 7, DateTimeOffset.UtcNow, Token);
        Assert.True(usage.RentedHours > 23); Assert.True(usage.RentedHours < 25);
        Assert.IsType<ConflictObjectResult>(await controller.SaveIncidentReport(booking.Id, incident.Id, request, Token));
        Assert.Single(await db.AssetInspections.Where(x=>x.AssetId==replacement.Id).ToListAsync(Token));
        var returns = new RentalOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = controller.ControllerContext };
        var returnRequest = new ReturnInspectionRequest(replacement.AssetNumber, true, true, 1006, 100,
            "Replacement returned in satisfactory condition.", null, "Customer", 0, null, 0, 0, 0, 0, 0, 0, null, PaymentMethod.Cash)
            { ReturnedAt = DateTimeOffset.UtcNow, SignatureDataUrl = Photo, ChecklistItems = ["Checked"], EvidenceDataUrls = [Photo] };
        Assert.IsType<OkObjectResult>(await returns.Return(booking.Id, returnRequest, Token));
        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.Equal(AssetStatus.Available, replacement.Status); Assert.Equal(AssetStatus.Maintenance, asset.Status);
        Assert.Equal(1006, replacement.CurrentMeterReading);

    }

    [Fact]
    public async Task Reserved_replacement_cannot_be_taken_even_if_the_browser_previously_listed_it()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, booking, asset, incident) = await Setup(db);
        var replacement = new Asset { AssetNumber = "BUSY", Name = "Busy vehicle", BranchId = asset.BranchId, Type = asset.Type };
        db.Add(replacement); db.Add(new Booking { BookingNumber = "NEXT-CUSTOMER", Status = BookingStatus.Confirmed,
            Items = [new BookingItem { AssetId = replacement.Id, StartAt = DateTimeOffset.UtcNow.AddHours(1), EndAt = DateTimeOffset.UtcNow.AddDays(1) }] });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        Assert.IsType<ConflictObjectResult>(await controller.SaveIncidentReport(booking.Id, incident.Id,
            Report(asset, RentalIncidentOutcome.ReplaceAsset) with { ReplacementAssetId = replacement.Id }, Token));
        Assert.Equal(asset.Id, booking.Items.Single().AssetId); Assert.Null(incident.Outcome);
    }

    [Fact]
    public async Task New_serious_staff_report_reuses_incident_settlement_without_losing_report_details()
    {
        await using var db = MaintenanceJobsTests.Database(); var (controller, booking, asset, _) = await Setup(db);
        Assert.IsType<OkObjectResult>(await controller.SaveIncidentReport(booking.Id, null,
            Report(asset, RentalIncidentOutcome.EndHire) with { Type = IncidentType.Accident }, Token));
        Assert.Equal(BookingStatus.Completed, booking.Status); Assert.NotNull(booking.IncidentClosedAt);
        var report = await db.RentalIncidents.SingleAsync(x=>x.Outcome==RentalIncidentOutcome.EndHire, Token);
        Assert.Equal("Staff report of the vehicle damage.", report.Description); Assert.Equal("POL-REFERENCE", report.PoliceReference);
        Assert.Contains(Photo, SharedRentalIncidents.Evidence(report)); Assert.Contains("brakes", report.InspectionJson);
        Assert.Single(await db.ManagementTasks.Where(x=>x.SourceEntityId==booking.Id&&!x.IsCompleted).ToListAsync(Token));
        Assert.Equal(100, booking.BondAmountHeld); Assert.Null(booking.BondSettledAt);
        Assert.Empty(await db.RentalInspections.Where(x=>x.Type==InspectionType.Return).ToListAsync(Token));
    }
}
