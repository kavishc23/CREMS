using System.Text.Json;
using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class CustomCategoryInspectionContextTests
{
    [Theory]
    [InlineData(AssetType.LightEquipment, "hours", MeterType.OperatingHours)]
    [InlineData(AssetType.HeavyEquipment, "hours", MeterType.EngineHours)]
    [InlineData(AssetType.PassengerVehicle, "km", MeterType.Odometer)]
    public void Projector_usage_is_not_recorded_as_engine_running_time(AssetType type, string unit, MeterType expected)
    {
        var asset = new Asset { AssetNumber = "TEST", Name = "Asset", Type = type, MeterUnit = unit };
        Assert.Equal(expected, AssetCategoryPolicy.MeterKind(asset));
    }

    [Theory]
    [InlineData(AssetType.PassengerVehicle, "SEDAN", false, true)]
    [InlineData(AssetType.PassengerVehicle, "HATCHBACK", false, true)]
    [InlineData(AssetType.PassengerVehicle, "SUV", false, true)]
    [InlineData(AssetType.CommercialVehicle, "PICKUP", false, true)]
    [InlineData(AssetType.PassengerVehicle, "SEDAN", true, false)]
    [InlineData(AssetType.LightEquipment, "PROJECTOR", false, false)]
    public async Task Custom_categories_use_vehicle_type_and_personnel_for_licence_requirements(
        AssetType type, string categoryCode, bool driver, bool expected)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = MaintenanceJobsTests.Database(); var (staff, asset) = await MaintenanceJobsTests.Setup(db);
        var category = new AssetCategory { Code = categoryCode, Name = categoryCode, DefaultAssetType = type,
            DefaultMeterType = type == AssetType.LightEquipment ? "hours" : "km" };
        var service = new ServiceOffering { Code = "HIRE", Name = "Hire", Type = type == AssetType.LightEquipment ? ServiceOfferingType.EquipmentHire : ServiceOfferingType.VehicleRental,
            InspectionRequirementsJson = type == AssetType.LightEquipment ? "{\"tracksMeter\":true,\"tracksFuel\":false}" : "{\"tracksMeter\":true,\"tracksFuel\":true}" };
        asset.Type = type; asset.AssetCategoryId = category.Id; asset.AssetCategory = category;
        asset.ServiceOfferingId = service.Id; asset.ServiceOffering = service;
        db.AddRange(category, service);
        var booking = new Booking { BookingNumber = "CUSTOM-HIRE", BranchId = asset.BranchId, Status = BookingStatus.Confirmed };
        db.BookingItems.Add(new BookingItem { AssetId = asset.Id, Booking = booking, BookingId = booking.Id,
            StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddDays(1) });
        if (driver) booking.Charges.Add(new BookingCharge { Description = "Professional driver", Category = ChargeCategory.Driver });
        using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        var controller = new RentalOperationsController(db, new CurrentStaffScope(db)) { ControllerContext = staff.ControllerContext };
        var result = Assert.IsType<OkObjectResult>(await controller.InspectionContext(booking.Id, token));
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(expected, json.GetProperty("requiresDrivingLicence").GetBoolean());
        Assert.True(json.GetProperty("tracksMeter").GetBoolean());
        Assert.Equal(type != AssetType.LightEquipment, json.GetProperty("tracksFuel").GetBoolean());
    }
}
