using CREMS.Api.Domain.Assets;

namespace CREMS.Api.Domain.Common;

// Initial catalogue defaults only. Saved staff settings and booking snapshots take precedence.
// Sources and business assumptions: docs/rental-pricing-defaults.md.
public static class FijiRentalDefaults
{
    public const decimal VatRate = 12.5m;

    public static decimal SuggestedBond(AssetType type, string name) => type switch
    {
        AssetType.Vehicle or AssetType.PassengerVehicle or AssetType.CommercialVehicle =>
            name.Contains("Sedan", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Hatchback", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Wagon", StringComparison.OrdinalIgnoreCase) ? 1000m :
            name.Contains("Coach", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Cargo", StringComparison.OrdinalIgnoreCase) ? 3000m : 2000m,
        AssetType.HeavyEquipment => 5000m,
        AssetType.MaterialHandlingEquipment or AssetType.PowerEquipment => 2000m,
        AssetType.Scaffolding => 1000m,
        AssetType.PortableSanitation or AssetType.WasteContainer => 300m,
        _ => 500m,
    };
}
