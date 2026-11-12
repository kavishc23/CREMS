using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class RentalEnhancementTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task Audit_history_pages_all_modules_and_applies_combined_filters()
    {
        await using var db = MaintenanceJobsTests.Database();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 55; i++) db.AuditEvents.Add(new AuditEvent {
            UserName = i % 2 == 0 ? "Kavish" : "Rahul", Action = "Booking updated", EntityType = "Booking",
            Summary = $"Tracked change {i}", OccurredAt = now.AddMinutes(-i), PreviousValues = "old", NewValues = "new" });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var controller = new AdministrationController(db, new CurrentStaffScope(db), new ConfigurationBuilder().Build(), null!);
        var first = Json(Assert.IsType<OkObjectResult>(await controller.AuditHistory(token: Token)).Value);
        var second = Json(Assert.IsType<OkObjectResult>(await controller.AuditHistory(page: 2, token: Token)).Value);
        Assert.Equal(55, first.GetProperty("total").GetInt32());
        Assert.Equal(20, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Empty(first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())
            .Intersect(second.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())));
        var filtered = Json(Assert.IsType<OkObjectResult>(await controller.AuditHistory(actor: "Kavish", action: "updated", entityType: "Booking", from: now.AddMinutes(-10), to: now, pageSize: 50, token: Token)).Value);
        Assert.Equal(6, filtered.GetProperty("total").GetInt32());
        Assert.False(filtered.GetProperty("hasMore").GetBoolean());
        Assert.Equal("old", first.GetProperty("items")[0].GetProperty("previousValues").GetString());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 500)]
    [InlineData(1000001, 20)]
    public async Task Audit_rejects_invalid_page_parameters(int page, int pageSize)
    {
        await using var db = MaintenanceJobsTests.Database();
        var controller = new AdministrationController(db, new CurrentStaffScope(db), new ConfigurationBuilder().Build(), null!);
        Assert.IsType<BadRequestObjectResult>(await controller.AuditHistory(page: page, pageSize: pageSize, token: Token));
        Assert.IsType<BadRequestObjectResult>(await controller.AuditHistory(from: DateTimeOffset.UtcNow, to: DateTimeOffset.UtcNow.AddDays(-1), token: Token));
    }

    [Fact]
    public async Task Inspection_context_exposes_verified_licence_then_preserves_the_driver_at_handover()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        asset.Type = AssetType.PassengerVehicle;
        var customer = new Customer { CustomerNumber = "C-1", Name = "Customer" };
        var booking = new Booking { BookingNumber = "B-1", Customer = customer, CustomerId = customer.Id, BranchId = asset.BranchId };
        booking.Items.Add(new BookingItem { Asset = asset, AssetId = asset.Id });
        db.Add(booking);
        db.CustomerLicences.Add(new CustomerLicence { CustomerId = customer.Id, StorageKey = "test", ContentType = "image/png", ContentHash = "hash", LicenceNumber = "123456", LicenceClasses = "2,3", Status = LicenceVerificationStatus.Verified });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var controller = new RentalOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        var before = Json(Assert.IsType<OkObjectResult>(await controller.InspectionContext(booking.Id, Token)).Value);
        Assert.Equal("123456", before.GetProperty("driverLicence").GetProperty("number").GetString());
        Assert.Equal("2,3", before.GetProperty("driverLicence").GetProperty("licenceClass").GetString());
        db.AuthorizedDrivers.Add(new AuthorizedDriver { BookingId = booking.Id, FullName = "Actual driver", LicenceNumber = "987654", LicenceClass = "2", LicenceExpiry = new DateOnly(2028, 1, 1), IsPrimary = true, Verified = true });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var after = Json(Assert.IsType<OkObjectResult>(await controller.InspectionContext(booking.Id, Token)).Value);
        Assert.Equal("987654", after.GetProperty("driverLicence").GetProperty("number").GetString());
        Assert.Equal("2028-01-01", after.GetProperty("driverLicence").GetProperty("expiry").GetString());
    }

    [Theory]
    [InlineData("available")]
    [InlineData("maintenance")]
    [InlineData("overlap")]
    [InlineData("turnaround")]
    public async Task Replacement_suggestions_and_submission_use_the_same_availability_rules(string scenario)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var customer = new Customer { CustomerNumber = "C-1", Name = "Customer" };
        var now = DateTimeOffset.UtcNow;
        var booking = new Booking { BookingNumber = "B-1", Customer = customer, CustomerId = customer.Id, BranchId = asset.BranchId, Status = BookingStatus.Confirmed };
        booking.Items.Add(new BookingItem { Asset = asset, AssetId = asset.Id, StartAt = now, EndAt = now.AddDays(1), DailyRate = 100 });
        var replacement = new Asset { AssetNumber = "TEST-2", Name = "Replacement", BranchId = asset.BranchId, TurnaroundMinutes = 60 };
        db.AddRange(booking, replacement);
        if (scenario == "maintenance") db.MaintenanceJobs.Add(new MaintenanceJob { AssetId = replacement.Id, BranchId = replacement.BranchId, JobNumber = "M-1", ServiceType = "Repair" });
        if (scenario is "overlap" or "turnaround") {
            var other = new Booking { BookingNumber = "B-2", BranchId = asset.BranchId, Status = BookingStatus.Confirmed };
            other.Items.Add(new BookingItem { AssetId = replacement.Id, StartAt = scenario == "overlap" ? now : now.AddDays(1).AddMinutes(30), EndAt = now.AddDays(2) });
            db.Add(other);
        }
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var controller = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        var options = Json(Assert.IsType<OkObjectResult>(await controller.GetReplacementAssets(booking.Id, Token)).Value);
        Assert.Equal(scenario == "available" ? 1 : 0, options.GetArrayLength());
        var result = await controller.ResolveConfirmedException(booking.Id, new ResolveBookingExceptionRequest(replacement.Id, "Customer agrees to replacement after inspection"), Token);
        if (scenario == "available") { Assert.IsType<OkObjectResult>(result); Assert.Equal(replacement.Id, booking.Items.Single().AssetId); Assert.Equal(100, booking.Items.Single().DailyRate); }
        else { Assert.IsType<ConflictObjectResult>(result); Assert.Equal(asset.Id, booking.Items.Single().AssetId); }
    }
    [Theory]
    [InlineData(false, true, "Customer accepts the existing scratch", false, false)]
    [InlineData(true, false, "Customer accepts the existing scratch", false, false)]
    [InlineData(true, true, "short", false, false)]
    [InlineData(true, true, "Customer accepts the existing scratch", true, false)]
    [InlineData(true, true, "Customer accepts the existing scratch", false, true)]
    public async Task Pickup_damage_requires_explicit_acceptance_safety_confirmation_and_note_and_keeps_maintenance_blockers(
        bool accept, bool safe, string note, bool maintenance, bool succeeds)
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        asset.Type = AssetType.PortableSanitation;
        var customer = new Customer { CustomerNumber = "C-1", Name = "Customer" };
        var booking = new Booking { BookingNumber = "B-1", Customer = customer, CustomerId = customer.Id, BranchId = asset.BranchId, Branch = asset.Branch, Status = BookingStatus.Confirmed };
        booking.Items.Add(new BookingItem { Asset = asset, AssetId = asset.Id, StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddDays(1), DailyRate = 0 });
        db.Add(booking);
        if (maintenance) db.MaintenanceJobs.Add(new MaintenanceJob { AssetId = asset.Id, BranchId = asset.BranchId, JobNumber = "M-1", ServiceType = "Repair" });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var request = JsonSerializer.Deserialize<PickupRentalRequest>("""
          {"scannedAssetNumber":"TEST-1","customerSignatureName":"Customer","customerAcceptedTerms":true,"agentApproved":true,
           "identificationVerified":true,"paymentVerified":true,"customerSignatureDataUrl":"data:image/png;base64,aGVsbG8=",
           "conditionNotes":"Inspected with customer","damageNotes":"Small existing scratch on outside panel",
           "evidenceDataUrls":["data:image/png;base64,aGVsbG8="],"checklistItems":["Clean"],"checklistAllItems":["Clean","Body"],"checklistFailedItems":["Body"]}
          """, new JsonSerializerOptions(JsonSerializerDefaults.Web))! with {
              AcceptExistingDamage = accept, SafeToOperate = safe, ExistingDamageAcceptanceNote = note };
        var controller = new RentalAgreementsController(db, new CurrentStaffScope(db), null!) { ControllerContext = staff.ControllerContext };
        var result = await controller.Pickup(booking.Id, request, Token);
        if (succeeds) {
            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(BookingStatus.ConvertedToRental, booking.Status);
            var inspection = await db.RentalInspections.SingleAsync(Token);
            Assert.True(JsonDocument.Parse(inspection.EvidenceJson!).RootElement.GetProperty("acceptedExistingDamage").GetBoolean());
            Assert.Contains(note, (await db.RentalAgreements.SingleAsync(Token)).TermsJson);
            Assert.Contains(await db.AuditEvents.ToListAsync(Token), x => x.Action == "Pickup existing damage accepted");
        } else {
            Assert.True(result is BadRequestObjectResult or ConflictObjectResult);
            Assert.Equal(BookingStatus.Confirmed, booking.Status);
            Assert.Empty(await db.RentalAgreements.ToListAsync(Token));
        }
    }

    [Fact]
    public async Task Staff_quotation_preserves_fee_units_rate_and_internal_cost()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var customer = new Customer { CustomerNumber = "C-1", Name = "Customer" };
        var booking = new Booking { BookingNumber = "B-1", Customer = customer, CustomerId = customer.Id, BranchId = asset.BranchId };
        booking.Items.Add(new BookingItem { Asset = asset, AssetId = asset.Id, StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddDays(1), DailyRate = 100 });
        db.Add(booking);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var controller = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        var result = await controller.PrepareQuotation(booking.Id, new PrepareBookingQuotationRequest(DateTimeOffset.UtcNow.AddDays(7), 0, 0, 0, "Electricity use", [
            new("Asset hire", 1, 100, 0, ChargeUnit.Day, ChargeCategory.BaseHire),
            new("Electricity Charge", 4, 10, 7, ChargeUnit.Unit, ChargeCategory.Other)]), Token);
        Assert.IsType<OkObjectResult>(result);
        var fee = await db.BookingCharges.SingleAsync(Token);
        Assert.Equal(4, fee.Quantity); Assert.Equal(40, fee.Quantity * fee.UnitRate); Assert.Equal(28, fee.Quantity * fee.UnitCost);
        Assert.Equal(140, (await db.SalesQuotes.SingleAsync(Token)).Total);
    }

    [Fact]
    public async Task Staff_workspace_lists_configured_requirement_and_uploaded_file_and_rejects_other_booking_download()
    {
        await using var db = MaintenanceJobsTests.Database();
        var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var customer = new Customer { CustomerNumber = "C-1", Name = "Customer" };
        var service = new ServiceOffering { Code = "EVENT", Name = "Event hire", RequiredDocumentsJson = "[\"Event Approval\"]" };
        asset.ServiceOffering = service; asset.ServiceOfferingId = service.Id;
        var booking = new Booking { BookingNumber = "B-1", Customer = customer, CustomerId = customer.Id, BranchId = asset.BranchId, Branch = asset.Branch };
        booking.Items.Add(new BookingItem { AssetId = asset.Id, Asset = asset, StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddDays(1) });
        var record = new DocumentRecord { DocumentNumber = "DOC-1", EntityType = nameof(Booking), EntityId = booking.Id, Type = "Event Approval", FileName = "approval.pdf", StoragePath = "test.pdf" };
        db.AddRange(service, booking, record);
        using (db.SuppressNotifications()) await db.SaveChangesAsync(Token);
        var controller = new BookingsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        var response = Json(Assert.IsType<OkObjectResult>(await controller.GetWorkspace(booking.Id, Token)).Value);
        Assert.Equal("Event Approval", response.GetProperty("requiredDocuments")[0].GetString());
        Assert.Equal("approval.pdf", response.GetProperty("documents")[0].GetProperty("fileName").GetString());
        var documents = new BookingDocumentsController(db, new CurrentStaffScope(db), null!) { ControllerContext = staff.ControllerContext };
        Assert.IsType<NotFoundResult>(await documents.Download(Guid.NewGuid(), record.Id, Token));
        Assert.IsType<NotFoundResult>(await documents.Download(booking.Id, Guid.NewGuid(), Token));
    }

}
