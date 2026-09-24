using CREMS.Api.Controllers;
using CREMS.Api.Domain.Assets;
using CREMS.Api.Domain.Common;
using CREMS.Api.Domain.Rentals;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class FijiRentalDefaultsTests
{
    [Fact]
    public void New_divisions_bookings_and_division_requests_use_current_vat()
    {
        Assert.Equal(12.5m, new Division { Code = "TEST", Name = "Test" }.DefaultTaxRate);
        Assert.Equal(12.5m, new Booking { BookingNumber = "TEST" }.TaxRate);
        Assert.Equal(12.5m, new SaveDivisionRequest("TEST", "Test", null, null, null, 0, true).DefaultTaxRate);
        Assert.Equal(30m, 240m * FijiRentalDefaults.VatRate / 100m);
    }

    [Theory]
    [InlineData(AssetType.PassengerVehicle, "Hyundai Grand i10 Sedan", 1000)]
    [InlineData(AssetType.PassengerVehicle, "Honda CR-V Sport AWD", 2000)]
    [InlineData(AssetType.CommercialVehicle, "Nissan NV350 Urvan 16 Seater", 2000)]
    [InlineData(AssetType.CommercialVehicle, "Isuzu NQR75L 37 Seater Coach", 3000)]
    [InlineData(AssetType.HeavyEquipment, "Excavator", 5000)]
    [InlineData(AssetType.MaterialHandlingEquipment, "Forklift", 2000)]
    [InlineData(AssetType.PowerEquipment, "Generator", 2000)]
    [InlineData(AssetType.LightEquipment, "Mixer", 500)]
    [InlineData(AssetType.Scaffolding, "Tower", 1000)]
    [InlineData(AssetType.PortableSanitation, "Toilet", 300)]
    [InlineData(AssetType.WasteContainer, "Bin", 300)]
    public void Initial_bonds_vary_by_category(AssetType type, string name, decimal expected) =>
        Assert.Equal(expected, FijiRentalDefaults.SuggestedBond(type, name));

    [Fact]
    public void Saved_waiver_and_custom_bond_override_starter_suggestions()
    {
        var asset = new Asset { AssetNumber = "TEST", Name = "Sedan", Type = AssetType.PassengerVehicle,
            InheritBond = false, DefaultBondAmount = 0 };
        Assert.Equal(0m, AssetCategoryPolicy.Bond(asset));
        asset.DefaultBondAmount = 750;
        Assert.Equal(750m, AssetCategoryPolicy.Bond(asset));
    }
}
