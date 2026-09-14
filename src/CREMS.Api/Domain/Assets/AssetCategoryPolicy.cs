using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Assets;

/// <summary>Operational behaviour derived from the configured asset category.</summary>
public static class AssetCategoryPolicy
{
    public static bool IsVehicle(AssetType type) => type is AssetType.Vehicle or AssetType.PassengerVehicle or AssetType.CommercialVehicle;
    public static bool IsEquipment(AssetType type) => !IsVehicle(type);
    public static string Code(Asset asset) => asset.AssetCategory?.Code?.ToUpperInvariant() ??
        (IsVehicle(asset.Type) ? "RENTAL_VEHICLE" : "GENERAL_EQUIPMENT");

    public static PersonnelRequirement Personnel(Asset asset) =>
        asset.AssetCategory?.PersonnelRequirement ?? asset.PersonnelRequirement;

    public static bool AllowsPersonnel(Asset asset) => IsVehicle(asset.Type) || Personnel(asset) != PersonnelRequirement.None;
    public static bool RequiresPersonnel(Asset asset) => Personnel(asset) == PersonnelRequirement.Required;
    public static bool RequiresDrivingLicence(Asset asset, bool professionalPersonnelProvided) =>
        IsVehicle(asset.Type) && !professionalPersonnelProvided;
    public static bool TracksFuel(Asset asset) => Code(asset) is
        "RENTAL_VEHICLE" or "FORKLIFT" or "GENSET" or "HEAVY_MACHINE" or "GENERAL_EQUIPMENT";
    public static bool TracksMeter(Asset asset) => Code(asset) is not
        ("PORTABLE_TOILET" or "SCAFFOLD" or "BIG_BIN");

    public static string InspectionGuidance(Asset asset) => Code(asset) switch
    {
        "RENTAL_VEHICLE" => "Check body, glass, tyres, lights, cabin, safety equipment, fuel and odometer.",
        "HEAVY_MACHINE" => "Check engine, hydraulics, tracks or tyres, cab, alarms, attachments, leaks and safety equipment.",
        "FORKLIFT" => "Check forks, mast, chains, hydraulics, tyres, brakes, horn, alarms and rated-capacity plate.",
        "GENSET" => "Check engine, battery, fuel, cooling, control panel, breakers, cables, earth stake and leaks.",
        "SCAFFOLD" => "Count frames, braces, platforms, pins, base plates and guardrails; check corrosion, distortion and damage.",
        "PORTABLE_TOILET" => "Check structure, tank, door, locks, vents, cleanliness, consumables and safe placement.",
        "BIG_BIN" => "Check body, floor, doors, lifting points, load limits, prohibited waste and safe placement.",
        _ => "Check condition, controls, guards, cables or hoses, accessories, safety labels and operation."
    };
}
